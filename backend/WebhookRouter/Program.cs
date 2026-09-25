using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WebhookRouter.Contracts;
using WebhookRouter.Data;
using WebhookRouter.Models;
using WebhookRouter.Services;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");

builder.Services.AddDbContext<WebhookDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddIdentity<AppUser, IdentityRole<Guid>>(options =>
    {
        options.User.RequireUniqueEmail = true;
        // A verified email OR explicit administrator approval permits password sign-in.
        options.SignIn.RequireConfirmedAccount = true;
        options.Password.RequiredLength = 12;
        options.Lockout.MaxFailedAccessAttempts = 5;
        options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
    })
    .AddEntityFrameworkStores<WebhookDbContext>()
    .AddDefaultTokenProviders();
builder.Services.AddScoped<IUserConfirmation<AppUser>, PasswordAccountConfirmation>();
var jwtSessions = new JwtSessionService(builder.Configuration);
builder.Services.AddSingleton(jwtSessions);
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.MapInboundClaims = false;
        options.TokenValidationParameters = jwtSessions.ValidationParameters;
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && context.Request.Path.StartsWithSegments("/hubs/events"))
                    context.Token = token;
                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                if (!Guid.TryParse(context.Principal?.FindFirstValue("sub"), out var id))
                { context.Fail("Invalid application user."); return; }
                var manager = context.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
                var user = await manager.FindByIdAsync(id.ToString());
                if (user is null || !user.IsEnabled || string.IsNullOrEmpty(user.SecurityStamp)
                    || user.SecurityStamp != context.Principal?.FindFirstValue("security_stamp"))
                { context.Fail("This application session is no longer valid."); return; }
                // Reload roles so role removals take effect without waiting for JWT expiration.
                if (context.Principal?.Identity is ClaimsIdentity identity)
                {
                    foreach (var claim in identity.FindAll(ClaimTypes.NameIdentifier).Concat(identity.FindAll("role")).ToArray())
                        identity.RemoveClaim(claim);
                    identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()));
                    foreach (var role in await manager.GetRolesAsync(user)) identity.AddClaim(new Claim("role", role));
                }
            }
        };
    });
builder.AddGoogleSignIn();
var microsoftEnabled = builder.AddMicrosoftSignIn();
builder.Services.AddAuthorization(options => options.AddPolicy(AppRoles.ManageUsers, policy => policy.RequireRole(AppRoles.GlobalAdmin)));
builder.Services.AddSingleton<ServiceBusPublisher>();
builder.Services.AddSingleton<ConcurrentQueue<ReceivedRequest>>();
builder.Services.AddSignalR();
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("password-auth", limiter =>
    {
        limiter.PermitLimit = 60;
        limiter.Window = TimeSpan.FromMinutes(1);
        limiter.QueueLimit = 0;
    });
});
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<WebhookDbContext>().Database.MigrateAsync();
    var roleManager = scope.ServiceProvider.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
    foreach (var role in new[] { AppRoles.GlobalAdmin, AppRoles.User })
    {
        if (!await roleManager.RoleExistsAsync(role))
        {
            var result = await roleManager.CreateAsync(new IdentityRole<Guid>(role));
            if (!result.Succeeded && !await roleManager.RoleExistsAsync(role))
                throw new InvalidOperationException($"Unable to initialize role {role}.");
        }
    }
    var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    var usersWithoutRoles = await db.Users.Where(user => !db.UserRoles.Any(role => role.UserId == user.Id)).ToListAsync();
    foreach (var user in usersWithoutRoles)
    {
        var result = await userManager.AddToRoleAsync(user, AppRoles.User);
        if (!result.Succeeded) throw new InvalidOperationException("Unable to initialize user role.");
    }
    // Supply only during initial setup; never automatically promote the first login.
    var adminEmail = builder.Configuration["BOOTSTRAP_GLOBAL_ADMIN_EMAIL"]?.Trim();
    if (!string.IsNullOrWhiteSpace(adminEmail))
    {
        var admin = await userManager.FindByEmailAsync(adminEmail);
        if (admin is null || !admin.IsEnabled)
            throw new InvalidOperationException("Bootstrap administrator must be an existing enabled user. Sign in first, then rerun setup.");
        if (!await userManager.IsInRoleAsync(admin, AppRoles.GlobalAdmin))
        {
            var result = await userManager.AddToRoleAsync(admin, AppRoles.GlobalAdmin);
            if (!result.Succeeded) throw new InvalidOperationException("Unable to initialize Global Admin.");
        }
    }
}

app.UseCors();
app.UseLoginAudit();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapPasswordSignIn();
app.MapActivityLog();
if (microsoftEnabled) app.MapMicrosoftSignIn();
app.MapGoogleSignIn();
app.MapGet("/healthz", () => Results.Ok(new { status = "Healthy" }));
app.MapGet("/", (ClaimsPrincipal user, ConcurrentQueue<ReceivedRequest> requests) =>
    requests.Where(x => x.CreatedByUserId == GetUserId(user)).Reverse().Take(500)).RequireAuthorization();
app.MapPost("/reset", (ClaimsPrincipal user, ConcurrentQueue<ReceivedRequest> requests) =>
{
    var userId = GetUserId(user);
    var retained = requests.Where(x => x.CreatedByUserId != userId).ToArray();
    requests.Clear();
    foreach (var item in retained) requests.Enqueue(item);
    return Results.NoContent();
}).RequireAuthorization();

app.MapGet("/api/auth/me", async (ClaimsPrincipal principal, UserManager<AppUser> userManager) =>
{
    if (principal.Identity?.IsAuthenticated != true) return Results.Unauthorized();
    var user = await userManager.GetUserAsync(principal);
    return user is null
        ? Results.Unauthorized()
        : Results.Ok(ToUserResponse(user, await userManager.GetRolesAsync(user)));
}).RequireAuthorization();

app.MapPut("/api/auth/me", async (UserProfileRequest request, ClaimsPrincipal principal, UserManager<AppUser> userManager) =>
{
    var firstName = NormalizeProfileName(request.FirstName);
    var lastName = NormalizeProfileName(request.LastName);
    var phoneNumber = NormalizeProfileName(request.PhoneNumber);
    if (firstName?.Length > 100 || lastName?.Length > 100)
        return Results.BadRequest(new { error = "First name and last name must each be at most 100 characters." });
    if (phoneNumber?.Length > 50)
        return Results.BadRequest(new { error = "Phone number must be at most 50 characters." });

    var user = await userManager.GetUserAsync(principal);
    if (user is null) return Results.Unauthorized();
    user.FirstName = firstName;
    user.LastName = lastName;
    if (!string.Equals(user.PhoneNumber, phoneNumber, StringComparison.Ordinal))
    {
        user.PhoneNumber = phoneNumber;
        user.PhoneNumberConfirmed = false;
    }
    var result = await userManager.UpdateAsync(user);
    return result.Succeeded
        ? Results.Ok(ToUserResponse(user, await userManager.GetRolesAsync(user)))
        : Results.BadRequest(new { error = string.Join(" ", result.Errors.Select(x => x.Description)) });
}).RequireAuthorization();

var tenants = app.MapGroup("/api/tenants").RequireAuthorization();

var users = app.MapGroup("/api/users").RequireAuthorization(AppRoles.ManageUsers);
users.MapPut("/{userId:guid}/password-authentication", (Guid userId, ManagePasswordAuthenticationRequest request,
    ClaimsPrincipal principal, UserManager<AppUser> manager, WebhookDbContext db, CancellationToken ct) =>
    UserAdministration.SavePasswordAuthenticationAsync(userId, request, principal, manager, db, ct))
    .RequireRateLimiting("password-auth");
users.MapPost("/", (ManageUserRequest request, ClaimsPrincipal principal, UserManager<AppUser> manager, WebhookDbContext db, CancellationToken ct) =>
    UserAdministration.SaveAsync(null, request, principal, manager, db, ct));
users.MapPut("/{userId:guid}", (Guid userId, ManageUserRequest request, ClaimsPrincipal principal, UserManager<AppUser> manager, WebhookDbContext db, CancellationToken ct) =>
    UserAdministration.SaveAsync(userId, request, principal, manager, db, ct));
users.MapGet("/", async (WebhookDbContext db, CancellationToken ct) =>
{
    var accounts = await db.Users.AsNoTracking().OrderBy(x => x.Email)
        .Select(x => new { x.Id, username = x.UserName, x.Email, x.FirstName, x.LastName, x.PhoneNumber, x.IsEnabled,
            x.AllowPasswordAuthentication, x.LockoutEnabled, x.LockoutEnd, x.AccessFailedCount, HasPassword = x.PasswordHash != null,
            HasExternalLogin = db.UserLogins.Any(login => login.UserId == x.Id) }).ToListAsync(ct);
    var memberships = await (from membership in db.UserRoles
        join role in db.Roles on membership.RoleId equals role.Id
        select new { membership.UserId, role.Name }).ToListAsync(ct);
    var rolesByUser = memberships.ToLookup(x => x.UserId, x => x.Name);
    return Results.Ok(accounts.Select(x => new { x.Id, x.username, x.Email, x.FirstName, x.LastName, x.PhoneNumber, x.IsEnabled, x.AllowPasswordAuthentication, x.LockoutEnabled, x.LockoutEnd, x.AccessFailedCount, x.HasPassword, x.HasExternalLogin, roles = rolesByUser[x.Id].ToArray() }));
});
users.MapPatch("/{userId:guid}/enabled", async (Guid userId, EnabledRequest request, ClaimsPrincipal principal, WebhookDbContext db, CancellationToken ct) =>
{
    if (userId == GetUserId(principal) && !request.IsEnabled)
        return Results.BadRequest(new { error = "You cannot disable your own account." });
    // Serialize administrator status changes to prevent disabling the last enabled admin concurrently.
    await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
    var account = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct);
    if (account is null) return Results.NotFound();
    var adminIds = from membership in db.UserRoles join role in db.Roles on membership.RoleId equals role.Id
                   where role.Name == AppRoles.GlobalAdmin select membership.UserId;
    if (!request.IsEnabled && await adminIds.ContainsAsync(userId, ct)
        && !await db.Users.AnyAsync(x => x.Id != userId && x.IsEnabled && adminIds.Contains(x.Id), ct))
        return Results.Conflict(new { error = "At least one Global Admin must remain enabled." });
    if (account.IsEnabled != request.IsEnabled)
        ActivityAudit.Add(db, request.IsEnabled ? "AccountEnabled" : "AccountDisabled", account, principal);
    account.IsEnabled = request.IsEnabled;
    await db.SaveChangesAsync(ct);
    await transaction.CommitAsync(ct);
    return Results.NoContent();
});

tenants.MapGet("/", async (ClaimsPrincipal user, WebhookDbContext db, CancellationToken ct) =>
{
    var userId = GetUserId(user);
    var isGlobalAdmin = user.IsInRole(AppRoles.GlobalAdmin);
    return await db.Tenants.AsNoTracking().Where(x => isGlobalAdmin || x.CreatedByUserId == userId).OrderBy(x => x.Name)
        .Select(x => new TenantResponse(x.Id, x.Name, x.IsEnabled, x.CreatedAt, x.UpdatedAt, x.Topics.Count,
            new AuditUserResponse(x.CreatedByUser.Id, x.CreatedByUser.FirstName, x.CreatedByUser.LastName, x.CreatedByUser.Email),
            x.UpdatedByUser == null ? null : new AuditUserResponse(x.UpdatedByUser.Id, x.UpdatedByUser.FirstName, x.UpdatedByUser.LastName, x.UpdatedByUser.Email)))
        .ToListAsync(ct);
});

tenants.MapPost("/", async (TenantRequest request, ClaimsPrincipal user, WebhookDbContext db, CancellationToken ct) =>
{
    var error = ValidateTenant(request);
    if (error is not null) return error;
    var tenant = new Tenant { CreatedByUserId = GetUserId(user), UpdatedByUserId = GetUserId(user), Name = request.Name.Trim(), IsEnabled = request.IsEnabled };
    await using var transaction = await db.Database.BeginTransactionAsync(ct);
    db.Tenants.Add(tenant);
    await db.SaveChangesAsync(ct);
    RouteActivityAudit.Add(db, "Created", tenant, user, new { after = RouteActivityAudit.State(tenant) });
    await db.SaveChangesAsync(ct);
    await transaction.CommitAsync(ct);
    await db.Entry(tenant).Reference(x => x.CreatedByUser).LoadAsync(ct);
    await db.Entry(tenant).Reference(x => x.UpdatedByUser).LoadAsync(ct);
    return Results.Created($"/api/tenants/{tenant.Id}", ToResponse(tenant, 0));
});

tenants.MapPut("/{tenantId:guid}", async (Guid tenantId, TenantRequest request, ClaimsPrincipal user, WebhookDbContext db, CancellationToken ct) =>
{
    var error = ValidateTenant(request);
    if (error is not null) return error;
    var userId = GetUserId(user);
    var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Id == tenantId && x.CreatedByUserId == userId, ct);
    if (tenant is null) return Results.NotFound();
    var before = RouteActivityAudit.State(tenant);
    var wasEnabled = tenant.IsEnabled;
    var nameChanged = tenant.Name != request.Name.Trim();
    tenant.Name = request.Name.Trim();
    tenant.IsEnabled = request.IsEnabled;
    tenant.UpdatedByUserId = userId;
    tenant.UpdatedAt = DateTimeOffset.UtcNow;
    if (nameChanged)
        RouteActivityAudit.Add(db, "Updated", tenant, user, new { before, after = RouteActivityAudit.State(tenant) });
    if (wasEnabled != tenant.IsEnabled)
        RouteActivityAudit.Add(db, tenant.IsEnabled ? "Enabled" : "Disabled", tenant, user, new { before, after = RouteActivityAudit.State(tenant) });
    await db.SaveChangesAsync(ct);
    await db.Entry(tenant).Reference(x => x.CreatedByUser).LoadAsync(ct);
    await db.Entry(tenant).Reference(x => x.UpdatedByUser).LoadAsync(ct);
    return Results.Ok(ToResponse(tenant, await db.Topics.CountAsync(x => x.TenantId == tenantId, ct)));
});

tenants.MapPatch("/{tenantId:guid}/enabled", async (Guid tenantId, EnabledRequest request, ClaimsPrincipal user, WebhookDbContext db, CancellationToken ct) =>
{
    var userId = GetUserId(user);
    var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Id == tenantId && x.CreatedByUserId == userId, ct);
    if (tenant is null) return Results.NotFound();
    if (tenant.IsEnabled == request.IsEnabled) return Results.NoContent();
    RouteActivityAudit.Add(db, request.IsEnabled ? "Enabled" : "Disabled", tenant, user,
        new { before = tenant.IsEnabled, after = request.IsEnabled });
    tenant.IsEnabled = request.IsEnabled;
    tenant.UpdatedAt = DateTimeOffset.UtcNow;
    tenant.UpdatedByUserId = userId;
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
});

tenants.MapDelete("/{tenantId:guid}", async (Guid tenantId, ClaimsPrincipal user, WebhookDbContext db, CancellationToken ct) =>
{
    var userId = GetUserId(user);
    await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
    var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Id == tenantId && x.CreatedByUserId == userId, ct);
    if (tenant is null) return Results.NotFound();
    var deletedTopics = await db.Topics.Where(x => x.TenantId == tenantId).ToListAsync(ct);
    foreach (var deletedTopic in deletedTopics)
        RouteActivityAudit.Add(db, "Deleted", deletedTopic, user, new { reason = "TenantDeleted", before = RouteActivityAudit.State(deletedTopic) });
    RouteActivityAudit.Add(db, "Deleted", tenant, user, new { before = RouteActivityAudit.State(tenant), deletedTopicCount = deletedTopics.Count });
    db.Tenants.Remove(tenant);
    await db.SaveChangesAsync(ct);
    await transaction.CommitAsync(ct);
    return Results.NoContent();
});

tenants.MapGet("/{tenantId:guid}/topics", async (Guid tenantId, ClaimsPrincipal user, WebhookDbContext db, CancellationToken ct) =>
{
    var userId = GetUserId(user);
    var isGlobalAdmin = user.IsInRole(AppRoles.GlobalAdmin);
    if (!await db.Tenants.AnyAsync(x => x.Id == tenantId && (isGlobalAdmin || x.CreatedByUserId == userId), ct)) return Results.NotFound();
    var topics = await db.Topics.AsNoTracking().Include(x => x.CreatedByUser).Include(x => x.UpdatedByUser)
        .Where(x => x.TenantId == tenantId).OrderBy(x => x.Name).ToListAsync(ct);
    return Results.Ok(topics.Select(ToTopicResponse));
});

tenants.MapPost("/{tenantId:guid}/topics", async (Guid tenantId, TopicRequest request, ClaimsPrincipal user, WebhookDbContext db, CancellationToken ct) =>
{
    var error = ValidateTopic(request, isCreate: true, hasStoredConnection: false);
    if (error is not null) return error;
    var userId = GetUserId(user);
    if (!await db.Tenants.AnyAsync(x => x.Id == tenantId && x.CreatedByUserId == userId, ct)) return Results.NotFound();
    var key = NormalizeKey(request.Key);
    if (await db.Topics.AnyAsync(x => x.TenantId == tenantId && x.Key == key, ct))
        return Results.Conflict(new { error = "A topic with this key already exists in the tenant." });

    var topic = new Topic
    {
        TenantId = tenantId, Key = key, Name = request.Name.Trim(), IsEnabled = request.IsEnabled,
        CreatedByUserId = userId, UpdatedByUserId = userId,
        IsSharePointWebhook = request.IsSharePointWebhook,
        UseManagedIdentity = request.UseManagedIdentity,
        FullyQualifiedNamespace = request.UseManagedIdentity ? NormalizeNamespace(request.FullyQualifiedNamespace!) : null,
        ServiceBusConnectionString = request.UseManagedIdentity ? null : request.ServiceBusConnectionString!.Trim(),
        ServiceBusEntityType = NormalizeServiceBusEntityType(request.ServiceBusEntityType),
        ServiceBusEntityName = request.ServiceBusEntityName.Trim()
    };
    await using var transaction = await db.Database.BeginTransactionAsync(ct);
    db.Topics.Add(topic);
    await db.SaveChangesAsync(ct);
    RouteActivityAudit.Add(db, "Created", topic, user, new { after = RouteActivityAudit.State(topic) });
    await db.SaveChangesAsync(ct);
    await transaction.CommitAsync(ct);
    await db.Entry(topic).Reference(x => x.CreatedByUser).LoadAsync(ct);
    await db.Entry(topic).Reference(x => x.UpdatedByUser).LoadAsync(ct);
    return Results.Created($"/api/tenants/{tenantId}/topics/{topic.Id}", ToTopicResponse(topic));
});

tenants.MapPut("/{tenantId:guid}/topics/{topicId:guid}", async (Guid tenantId, Guid topicId, TopicRequest request, ClaimsPrincipal user, WebhookDbContext db, CancellationToken ct) =>
{
    var userId = GetUserId(user);
    var topic = await db.Topics.SingleOrDefaultAsync(x => x.Id == topicId && x.TenantId == tenantId && x.Tenant.CreatedByUserId == userId, ct);
    if (topic is null) return Results.NotFound();
    var error = ValidateTopic(request, isCreate: false, hasStoredConnection: !string.IsNullOrWhiteSpace(topic.ServiceBusConnectionString));
    if (error is not null) return error;
    var key = NormalizeKey(request.Key);
    if (await db.Topics.AnyAsync(x => x.TenantId == tenantId && x.Id != topicId && x.Key == key, ct))
        return Results.Conflict(new { error = "A topic with this key already exists in the tenant." });

    var before = RouteActivityAudit.State(topic);
    var wasEnabled = topic.IsEnabled;
    var previousConnectionString = topic.ServiceBusConnectionString;
    topic.Key = key;
    topic.UpdatedByUserId = userId;
    topic.Name = request.Name.Trim();
    topic.IsEnabled = request.IsEnabled;
    topic.IsSharePointWebhook = request.IsSharePointWebhook;
    topic.UseManagedIdentity = request.UseManagedIdentity;
    topic.FullyQualifiedNamespace = request.UseManagedIdentity ? NormalizeNamespace(request.FullyQualifiedNamespace!) : null;
    topic.ServiceBusEntityType = NormalizeServiceBusEntityType(request.ServiceBusEntityType);
    topic.ServiceBusEntityName = request.ServiceBusEntityName.Trim();
    if (request.UseManagedIdentity) topic.ServiceBusConnectionString = null;
    else if (!string.IsNullOrWhiteSpace(request.ServiceBusConnectionString)) topic.ServiceBusConnectionString = request.ServiceBusConnectionString.Trim();
    topic.UpdatedAt = DateTimeOffset.UtcNow;
    var connectionStringChanged = previousConnectionString != topic.ServiceBusConnectionString;
    var after = RouteActivityAudit.State(topic);
    if (!before.Equals(after) || connectionStringChanged)
        RouteActivityAudit.Add(db, "Updated", topic, user, new { before, after, connectionStringChanged });
    if (wasEnabled != topic.IsEnabled)
        RouteActivityAudit.Add(db, topic.IsEnabled ? "Enabled" : "Disabled", topic, user, new { before = wasEnabled, after = topic.IsEnabled });
    await db.SaveChangesAsync(ct);
    await db.Entry(topic).Reference(x => x.CreatedByUser).LoadAsync(ct);
    await db.Entry(topic).Reference(x => x.UpdatedByUser).LoadAsync(ct);
    return Results.Ok(ToTopicResponse(topic));
});

tenants.MapPatch("/{tenantId:guid}/topics/{topicId:guid}/enabled", async (Guid tenantId, Guid topicId, EnabledRequest request, ClaimsPrincipal user, WebhookDbContext db, CancellationToken ct) =>
{
    var userId = GetUserId(user);
    var topic = await db.Topics.SingleOrDefaultAsync(x => x.Id == topicId && x.TenantId == tenantId && x.Tenant.CreatedByUserId == userId, ct);
    if (topic is null) return Results.NotFound();
    if (topic.IsEnabled == request.IsEnabled) return Results.NoContent();
    RouteActivityAudit.Add(db, request.IsEnabled ? "Enabled" : "Disabled", topic, user,
        new { before = topic.IsEnabled, after = request.IsEnabled });
    topic.IsEnabled = request.IsEnabled;
    topic.UpdatedAt = DateTimeOffset.UtcNow;
    topic.UpdatedByUserId = userId;
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
});

tenants.MapDelete("/{tenantId:guid}/topics/{topicId:guid}", async (Guid tenantId, Guid topicId, ClaimsPrincipal user, WebhookDbContext db, CancellationToken ct) =>
{
    var userId = GetUserId(user);
    var topic = await db.Topics.SingleOrDefaultAsync(x => x.Id == topicId && x.TenantId == tenantId && x.Tenant.CreatedByUserId == userId, ct);
    if (topic is null) return Results.NotFound();
    RouteActivityAudit.Add(db, "Deleted", topic, user, new { before = RouteActivityAudit.State(topic) });
    db.Topics.Remove(topic);
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
});

app.MapPost("/tenants/{tenantId:guid}/topics/{topicKey}", async (
    Guid tenantId, string topicKey, HttpRequest request, WebhookDbContext db, ServiceBusPublisher publisher,
    ConcurrentQueue<ReceivedRequest> requests, IHubContext<WebhookHub> hub, CancellationToken ct) =>
{
    var normalizedTopic = NormalizeKey(topicKey);
    var topic = await db.Topics.AsNoTracking().Include(x => x.Tenant)
        .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Key == normalizedTopic, ct);
    if (topic is null) return Results.NotFound(new { error = "Webhook endpoint was not found." });
    if (!topic.Tenant.IsEnabled || !topic.IsEnabled)
        return Results.Json(new { error = "This webhook endpoint is disabled." }, statusCode: StatusCodes.Status403Forbidden);
    // SharePoint validation request: echo the token without publishing a message.
    if (topic.IsSharePointWebhook && request.Query.TryGetValue("validationtoken", out var token))
    {
        return Results.Text(token.ToString(), "text/plain");
    }

    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync(ct);
    try
    {
        await publisher.PublishAsync(topic.UseManagedIdentity, topic.FullyQualifiedNamespace, topic.ServiceBusConnectionString,
            topic.ServiceBusEntityName, body, topic.TenantId, topic.Key, request.ContentType, ct);
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "Failed to publish webhook for tenant {TenantId} and topic {TopicKey}", tenantId, topicKey);
        return Results.Problem("Azure Service Bus rejected or could not receive the message.", statusCode: StatusCodes.Status502BadGateway);
    }

    var received = new ReceivedRequest(Guid.NewGuid(), topic.TenantId.ToString(), topic.Key, DateTimeOffset.UtcNow, body, topic.Tenant.CreatedByUserId);
    requests.Enqueue(received);
    while (requests.Count > 500) requests.TryDequeue(out _);
    if (await db.Users.AnyAsync(x => x.Id == topic.Tenant.CreatedByUserId && x.IsEnabled, ct))
        await hub.Clients.User(topic.Tenant.CreatedByUserId.ToString()).SendAsync("WebhookReceived", received, ct);
    return Results.Accepted(value: received);
});

app.MapHub<WebhookHub>("/hubs/events", options => options.CloseOnAuthenticationExpiration = true).RequireAuthorization();
app.Run();

static IResult? ValidateTenant(TenantRequest request)
{
    if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200) return Results.BadRequest(new { error = "Tenant name is required and must be at most 200 characters." });
    return null;
}

static IResult? ValidateTopic(TopicRequest request, bool isCreate, bool hasStoredConnection)
{
    if (!ValidKey(request.Key)) return Results.BadRequest(new { error = "Topic key must be 1-100 letters, numbers, or hyphens." });
    if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200) return Results.BadRequest(new { error = "Topic name is required and must be at most 200 characters." });
    if (!ValidServiceBusEntityType(request.ServiceBusEntityType)) return Results.BadRequest(new { error = "Azure Service Bus destination type must be Topic or Queue." });
    if (string.IsNullOrWhiteSpace(request.ServiceBusEntityName) || request.ServiceBusEntityName.Trim().Length > 260) return Results.BadRequest(new { error = "Azure Service Bus entity name is required and must be at most 260 characters." });
    if (request.UseManagedIdentity && string.IsNullOrWhiteSpace(request.FullyQualifiedNamespace)) return Results.BadRequest(new { error = "Fully qualified namespace is required for managed identity." });
    if (request.UseManagedIdentity && request.FullyQualifiedNamespace!.Trim().Length > 300) return Results.BadRequest(new { error = "Fully qualified namespace must be at most 300 characters." });
    if (!request.UseManagedIdentity && string.IsNullOrWhiteSpace(request.ServiceBusConnectionString) && (isCreate || !hasStoredConnection)) return Results.BadRequest(new { error = "Connection string is required when managed identity is disabled." });
    if (!string.IsNullOrWhiteSpace(request.ServiceBusConnectionString) && request.ServiceBusConnectionString.Trim().Length > 2000) return Results.BadRequest(new { error = "Connection string must be at most 2000 characters." });
    return null;
}

static bool ValidKey(string? key) => !string.IsNullOrWhiteSpace(key) && key.Trim().Length <= 100 && Regex.IsMatch(key.Trim(), "^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.IgnoreCase);
static bool ValidServiceBusEntityType(string? value) => string.Equals(value, "Topic", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "Queue", StringComparison.OrdinalIgnoreCase);
static string NormalizeKey(string value) => value.Trim().ToLowerInvariant();
static string NormalizeNamespace(string value) => value.Trim().Replace("https://", "", StringComparison.OrdinalIgnoreCase).TrimEnd('/');
static string NormalizeServiceBusEntityType(string value) => string.Equals(value, "Queue", StringComparison.OrdinalIgnoreCase) ? "Queue" : "Topic";
static string? NormalizeProfileName(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
static Guid GetUserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
static object ToUserResponse(AppUser user, IList<string> roles) => new { id = user.Id, username = user.UserName, email = user.Email, firstName = user.FirstName, lastName = user.LastName, phoneNumber = user.PhoneNumber, isEnabled = user.IsEnabled, roles };
static AuditUserResponse? ToAuditUser(AppUser? user) => user is null ? null : new(user.Id, user.FirstName, user.LastName, user.Email);
static TenantResponse ToResponse(Tenant tenant, int count) => new(tenant.Id, tenant.Name, tenant.IsEnabled, tenant.CreatedAt, tenant.UpdatedAt, count, ToAuditUser(tenant.CreatedByUser), ToAuditUser(tenant.UpdatedByUser));
static TopicResponse ToTopicResponse(Topic topic) => new(topic.Id, topic.TenantId, topic.Key, topic.Name, topic.IsEnabled,
    topic.IsSharePointWebhook, topic.UseManagedIdentity, topic.FullyQualifiedNamespace, !string.IsNullOrWhiteSpace(topic.ServiceBusConnectionString),
    topic.ServiceBusEntityType, topic.ServiceBusEntityName, topic.CreatedAt, topic.UpdatedAt, ToAuditUser(topic.CreatedByUser), ToAuditUser(topic.UpdatedByUser));

public sealed record ReceivedRequest(
    Guid Id,
    string TenantId,
    string TopicName,
    DateTimeOffset ReceivedAt,
    string Payload,
    [property: JsonIgnore] Guid CreatedByUserId);
public sealed class WebhookHub : Hub;
public partial class Program;
