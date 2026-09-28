using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using WebhookRouter;
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
                {
                    context.Token = token;
                }

                return Task.CompletedTask;
            },
            OnTokenValidated = async context =>
            {
                if (!Guid.TryParse(context.Principal?.FindFirstValue("sub"), out var id))
                {
                    context.Fail("Invalid application user.");
                    return;
                }
                var manager = context.HttpContext.RequestServices.GetRequiredService<UserManager<AppUser>>();
                var user = await manager.FindByIdAsync(id.ToString());
                if (user is null || !user.IsEnabled || string.IsNullOrEmpty(user.SecurityStamp)
                    || user.SecurityStamp != context.Principal?.FindFirstValue("security_stamp"))
                {
                    context.Fail("This application session is no longer valid.");
                    return;
                }
                // Reload roles so role removals take effect without waiting for JWT expiration.
                if (context.Principal?.Identity is ClaimsIdentity identity)
                {
                    foreach (var claim in identity.FindAll(ClaimTypes.NameIdentifier).Concat(identity.FindAll("role")).ToArray())
                    {
                        identity.RemoveClaim(claim);
                    }

                    identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()));
                    foreach (var role in await manager.GetRolesAsync(user))
                    {
                        identity.AddClaim(new Claim("role", role));
                    }
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
            {
                throw new InvalidOperationException($"Unable to initialize role {role}.");
            }
        }
    }
    var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
    var userManager = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    var usersWithoutRoles = await db.Users.Where(user => !db.UserRoles.Any(role => role.UserId == user.Id)).ToListAsync();
    foreach (var user in usersWithoutRoles)
    {
        var result = await userManager.AddToRoleAsync(user, AppRoles.User);
        if (!result.Succeeded)
        {
            throw new InvalidOperationException("Unable to initialize user role.");
        }
    }
    // Supply only during initial setup; never automatically promote the first login.
    var adminEmail = builder.Configuration["BOOTSTRAP_GLOBAL_ADMIN_EMAIL"]?.Trim();
    if (!string.IsNullOrWhiteSpace(adminEmail))
    {
        var admin = await userManager.FindByEmailAsync(adminEmail);
        if (admin is null || !admin.IsEnabled)
        {
            throw new InvalidOperationException("Bootstrap administrator must be an existing enabled user. Sign in first, then rerun setup.");
        }

        if (!await userManager.IsInRoleAsync(admin, AppRoles.GlobalAdmin))
        {
            var result = await userManager.AddToRoleAsync(admin, AppRoles.GlobalAdmin);
            if (!result.Succeeded)
            {
                throw new InvalidOperationException("Unable to initialize Global Admin.");
            }
        }
    }
}

app.UseCors();
app.UseLoginAudit();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapEndpoints(microsoftEnabled);
app.Run();

public partial class Program;
