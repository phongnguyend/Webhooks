using System.Collections.Concurrent;
using System.Security.Claims;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WebhookServer.Contracts;
using WebhookServer.Data;
using WebhookServer.Models;
using WebhookServer.Services;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");

builder.Services.AddDbContext<WebhookDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddIdentity<AppUser, IdentityRole<Guid>>(options =>
    {
        options.User.RequireUniqueEmail = true;
        options.SignIn.RequireConfirmedEmail = true;
    })
    .AddEntityFrameworkStores<WebhookDbContext>()
    .AddDefaultTokenProviders();
var googleClientId = builder.Configuration["Authentication:Google:ClientId"] ?? string.Empty;
builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.Authority = "https://accounts.google.com";
        options.Audience = googleClientId;
        options.MapInboundClaims = false;
        options.TokenValidationParameters.NameClaimType = "email";
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var token = context.Request.Query["access_token"];
                if (!string.IsNullOrEmpty(token) && context.HttpContext.Request.Path.StartsWithSegments("/hubs/events"))
                    context.Token = token;
                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                var subject = context.Principal?.FindFirstValue("sub");
                var email = context.Principal?.FindFirstValue("email")?.Trim().ToLowerInvariant();
                var emailVerified = context.Principal?.FindFirstValue("email_verified");
                if (string.IsNullOrWhiteSpace(subject) || string.IsNullOrWhiteSpace(email) || !string.Equals(emailVerified, "true", StringComparison.OrdinalIgnoreCase))
                {
                    context.Fail("A verified Google email is required.");
                    return;
                }

                var userManager = context.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
                var user = await userManager.FindByLoginAsync("Google", subject) ?? await userManager.FindByEmailAsync(email);
                if (user is null)
                {
                    user = new AppUser
                    {
                        UserName = email,
                        Email = email,
                        EmailConfirmed = true,
                        FirstName = NormalizeProfileName(context.Principal?.FindFirstValue("given_name")),
                        LastName = NormalizeProfileName(context.Principal?.FindFirstValue("family_name"))
                    };
                    var createResult = await userManager.CreateAsync(user);
                    if (!createResult.Succeeded) { context.Fail("Unable to create the application user."); return; }
                }

                if ((await userManager.GetLoginsAsync(user)).All(x => x.LoginProvider != "Google" || x.ProviderKey != subject))
                {
                    var loginResult = await userManager.AddLoginAsync(user, new UserLoginInfo("Google", subject, "Google"));
                    if (!loginResult.Succeeded) { context.Fail("Unable to link the Google account."); return; }
                }

                if (context.Principal?.Identity is ClaimsIdentity identity)
                    identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()));
            }
        };
    });
builder.Services.AddAuthorization();
builder.Services.AddSingleton<ServiceBusPublisher>();
builder.Services.AddSingleton<ConcurrentQueue<ReceivedRequest>>();
builder.Services.AddSignalR();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader()
    .AllowAnyMethod()));

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<WebhookDbContext>().Database.MigrateAsync();
}

app.UseCors();
app.UseAuthentication();
app.UseAuthorization();
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
        : Results.Ok(ToUserResponse(user));
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
        ? Results.Ok(ToUserResponse(user))
        : Results.BadRequest(new { error = string.Join(" ", result.Errors.Select(x => x.Description)) });
}).RequireAuthorization();

var tenants = app.MapGroup("/api/tenants").RequireAuthorization();

tenants.MapGet("/", async (ClaimsPrincipal user, WebhookDbContext db, CancellationToken ct) =>
{
    var userId = GetUserId(user);
    return await db.Tenants.AsNoTracking().Where(x => x.CreatedByUserId == userId).OrderBy(x => x.Name)
        .Select(x => new TenantResponse(x.Id, x.Name, x.IsEnabled, x.CreatedAt, x.UpdatedAt, x.Topics.Count))
        .ToListAsync(ct);
});

tenants.MapPost("/", async (TenantRequest request, ClaimsPrincipal user, WebhookDbContext db, CancellationToken ct) =>
{
    var error = ValidateTenant(request);
    if (error is not null) return error;
    var tenant = new Tenant { CreatedByUserId = GetUserId(user), Name = request.Name.Trim(), IsEnabled = request.IsEnabled };
    db.Tenants.Add(tenant);
    await db.SaveChangesAsync(ct);
    return Results.Created($"/api/tenants/{tenant.Id}", ToResponse(tenant, 0));
});

tenants.MapPut("/{tenantId:guid}", async (Guid tenantId, TenantRequest request, ClaimsPrincipal user, WebhookDbContext db, CancellationToken ct) =>
{
    var error = ValidateTenant(request);
    if (error is not null) return error;
    var userId = GetUserId(user);
    var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Id == tenantId && x.CreatedByUserId == userId, ct);
    if (tenant is null) return Results.NotFound();
    tenant.Name = request.Name.Trim();
    tenant.IsEnabled = request.IsEnabled;
    tenant.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return Results.Ok(ToResponse(tenant, await db.Topics.CountAsync(x => x.TenantId == tenantId, ct)));
});

tenants.MapPatch("/{tenantId:guid}/enabled", async (Guid tenantId, EnabledRequest request, ClaimsPrincipal user, WebhookDbContext db, CancellationToken ct) =>
{
    var userId = GetUserId(user);
    var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Id == tenantId && x.CreatedByUserId == userId, ct);
    if (tenant is null) return Results.NotFound();
    tenant.IsEnabled = request.IsEnabled;
    tenant.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
});

tenants.MapDelete("/{tenantId:guid}", async (Guid tenantId, ClaimsPrincipal user, WebhookDbContext db, CancellationToken ct) =>
{
    var userId = GetUserId(user);
    var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Id == tenantId && x.CreatedByUserId == userId, ct);
    if (tenant is null) return Results.NotFound();
    db.Tenants.Remove(tenant);
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
});

tenants.MapGet("/{tenantId:guid}/topics", async (Guid tenantId, ClaimsPrincipal user, WebhookDbContext db, CancellationToken ct) =>
{
    var userId = GetUserId(user);
    if (!await db.Tenants.AnyAsync(x => x.Id == tenantId && x.CreatedByUserId == userId, ct)) return Results.NotFound();
    var topics = await db.Topics.AsNoTracking().Where(x => x.TenantId == tenantId).OrderBy(x => x.Name).ToListAsync(ct);
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
        IsSharePointWebhook = request.IsSharePointWebhook,
        UseManagedIdentity = request.UseManagedIdentity,
        FullyQualifiedNamespace = request.UseManagedIdentity ? NormalizeNamespace(request.FullyQualifiedNamespace!) : null,
        ServiceBusConnectionString = request.UseManagedIdentity ? null : request.ServiceBusConnectionString!.Trim(),
        ServiceBusEntityType = NormalizeServiceBusEntityType(request.ServiceBusEntityType),
        ServiceBusEntityName = request.ServiceBusEntityName.Trim()
    };
    db.Topics.Add(topic);
    await db.SaveChangesAsync(ct);
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

    topic.Key = key;
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
    await db.SaveChangesAsync(ct);
    return Results.Ok(ToTopicResponse(topic));
});

tenants.MapPatch("/{tenantId:guid}/topics/{topicId:guid}/enabled", async (Guid tenantId, Guid topicId, EnabledRequest request, ClaimsPrincipal user, WebhookDbContext db, CancellationToken ct) =>
{
    var userId = GetUserId(user);
    var topic = await db.Topics.SingleOrDefaultAsync(x => x.Id == topicId && x.TenantId == tenantId && x.Tenant.CreatedByUserId == userId, ct);
    if (topic is null) return Results.NotFound();
    topic.IsEnabled = request.IsEnabled;
    topic.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
});

tenants.MapDelete("/{tenantId:guid}/topics/{topicId:guid}", async (Guid tenantId, Guid topicId, ClaimsPrincipal user, WebhookDbContext db, CancellationToken ct) =>
{
    var userId = GetUserId(user);
    var topic = await db.Topics.SingleOrDefaultAsync(x => x.Id == topicId && x.TenantId == tenantId && x.Tenant.CreatedByUserId == userId, ct);
    if (topic is null) return Results.NotFound();
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
    await hub.Clients.User(topic.Tenant.CreatedByUserId.ToString()).SendAsync("WebhookReceived", received, ct);
    return Results.Accepted(value: received);
});

app.MapHub<WebhookHub>("/hubs/events").RequireAuthorization();
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
static object ToUserResponse(AppUser user) => new { id = user.Id, username = user.UserName, email = user.Email, firstName = user.FirstName, lastName = user.LastName, phoneNumber = user.PhoneNumber };
static TenantResponse ToResponse(Tenant tenant, int count) => new(tenant.Id, tenant.Name, tenant.IsEnabled, tenant.CreatedAt, tenant.UpdatedAt, count);
static TopicResponse ToTopicResponse(Topic topic) => new(topic.Id, topic.TenantId, topic.Key, topic.Name, topic.IsEnabled,
    topic.IsSharePointWebhook, topic.UseManagedIdentity, topic.FullyQualifiedNamespace, !string.IsNullOrWhiteSpace(topic.ServiceBusConnectionString),
    topic.ServiceBusEntityType, topic.ServiceBusEntityName, topic.CreatedAt, topic.UpdatedAt);

public sealed record ReceivedRequest(
    Guid Id,
    string TenantId,
    string TopicName,
    DateTimeOffset ReceivedAt,
    string Payload,
    [property: JsonIgnore] Guid CreatedByUserId);
public sealed class WebhookHub : Hub;
public partial class Program;
