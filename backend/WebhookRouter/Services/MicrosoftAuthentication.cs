using System.ComponentModel.DataAnnotations;
using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using WebhookRouter.Data;
using WebhookRouter.Models;

namespace WebhookRouter.Services;

public static class MicrosoftAuthentication
{
    private const string Scheme = "MicrosoftExchange";

    public static bool AddMicrosoftSignIn(this WebApplicationBuilder builder)
    {
        var clientId = builder.Configuration["Authentication:Microsoft:ClientId"];
        var tenantId = builder.Configuration["Authentication:Microsoft:TenantId"];
        if (string.IsNullOrWhiteSpace(clientId) && string.IsNullOrWhiteSpace(tenantId)) return false;
        if (!Guid.TryParse(clientId, out _) || !Guid.TryParse(tenantId, out var tenant))
            throw new InvalidOperationException("Microsoft ClientId and TenantId must both be configured as GUIDs.");
        var issuer = $"https://login.microsoftonline.com/{tenant:D}/v2.0";
        builder.Services.AddAuthentication().AddJwtBearer(Scheme, options =>
        {
            options.Authority = issuer;
            options.MapInboundClaims = false;
            options.TokenValidationParameters = new TokenValidationParameters
            {
                ValidateIssuer = true, ValidIssuer = issuer,
                ValidateAudience = true, ValidAudience = clientId,
                ValidateLifetime = true, RequireExpirationTime = true,
                ValidateIssuerSigningKey = true, RequireSignedTokens = true,
                ValidAlgorithms = [SecurityAlgorithms.RsaSha256], ClockSkew = TimeSpan.FromSeconds(30)
            };
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    if (!HttpMethods.IsPost(context.Request.Method)) context.NoResult();
                    else if (context.Request.Path == "/api/auth/link/microsoft")
                    {
                        var token = context.Request.Headers["X-Microsoft-Id-Token"].ToString();
                        if (string.IsNullOrEmpty(token)) context.NoResult();
                        else context.Token = token;
                    }
                    else if (context.Request.Path != "/api/auth/exchange/microsoft") context.NoResult();
                    return Task.CompletedTask;
                },
                OnTokenValidated = context =>
                {
                    var principal = context.Principal!;
                    if (!Guid.TryParse(principal.FindFirstValue("tid"), out var tid) || tid != tenant
                        || !Guid.TryParse(principal.FindFirstValue("oid"), out _)
                        || string.IsNullOrWhiteSpace(principal.FindFirstValue("sub"))
                        || string.IsNullOrWhiteSpace(principal.FindFirstValue("nonce"))
                        || principal.FindFirstValue("ver") != "2.0")
                        context.Fail("A Microsoft user ID token from the configured tenant is required.");
                    return Task.CompletedTask;
                }
            };
        });
        return true;
    }

    public static void MapMicrosoftSignIn(this WebApplication app)
    {
        app.MapPost("/api/auth/exchange/microsoft", (HttpContext context, UserManager<AppUser> manager, WebhookDbContext db, JwtSessionService sessions) =>
            Complete(context, context.User, null, manager, db, sessions))
            .RequireAuthorization(policy => policy.AddAuthenticationSchemes(Scheme).RequireAuthenticatedUser());

        app.MapPost("/api/auth/link/microsoft", async (HttpContext context, UserManager<AppUser> manager, WebhookDbContext db, JwtSessionService sessions) =>
        {
            // The existing internal JWT and the external ID token must both validate.
            var currentUser = await manager.GetUserAsync(context.User);
            if (currentUser is null || !currentUser.IsEnabled) return Results.Unauthorized();
            var external = await context.AuthenticateAsync(Scheme);
            if (!external.Succeeded) return Results.Unauthorized();
            return await Complete(context, external.Principal!, currentUser, manager, db, sessions);
        }).RequireAuthorization();
    }

    private static async Task<IResult> Complete(HttpContext context, ClaimsPrincipal external, AppUser? linkTo,
        UserManager<AppUser> manager, WebhookDbContext db, JwtSessionService sessions)
    {
        var key = $"{Guid.Parse(external.FindFirstValue("tid")!):D}:{Guid.Parse(external.FindFirstValue("oid")!):D}";
        await using var transaction = await db.Database.BeginTransactionAsync(context.RequestAborted);
        var user = await manager.FindByLoginAsync("Microsoft", key);
        if (linkTo is not null)
        {
            if (user is not null && user.Id != linkTo.Id)
                return Results.Conflict(new { error = "This Microsoft identity is already connected to another account." });
            user = linkTo;
        }
        if (user is null)
        {
            var email = external.FindFirstValue("email")?.Trim().ToLowerInvariant();
            if (string.IsNullOrWhiteSpace(email) || email.Length > 256 || !new EmailAddressAttribute().IsValid(email))
                return Results.BadRequest(new { error = "Microsoft did not provide an email. Configure the email ID-token claim, or sign in to your existing account and connect Microsoft from your profile." });
            // Microsoft email claims are not proof of mailbox ownership. Never merge by email.
            if (await manager.FindByEmailAsync(email) is not null)
                return Results.Conflict(new { error = "An account with this email already exists. Sign in using its current provider, then connect Microsoft from your profile." });
            user = new AppUser
            {
                Email = email, UserName = email, EmailConfirmed = false,
                FirstName = ProfileName(external.FindFirstValue("given_name") ?? external.FindFirstValue("name")),
                LastName = ProfileName(external.FindFirstValue("family_name"))
            };
            if (!(await manager.CreateAsync(user)).Succeeded) return Results.BadRequest(new { error = "Unable to create the application account." });
        }
        context.Items[ActivityAudit.TargetKey] = user;
        if (!user.IsEnabled) return Results.Unauthorized();
        var logins = await manager.GetLoginsAsync(user);
        if (logins.Any(login => login.LoginProvider == "Microsoft" && login.ProviderKey != key))
            return Results.Conflict(new { error = "This application account is already connected to a different Microsoft identity." });
        if (!logins.Any(login => login.LoginProvider == "Microsoft" && login.ProviderKey == key)
            && !(await manager.AddLoginAsync(user, new UserLoginInfo("Microsoft", key, "Microsoft"))).Succeeded)
            return Results.BadRequest(new { error = "Unable to connect the Microsoft identity." });
        var roles = await manager.GetRolesAsync(user);
        if (roles.Count == 0)
        {
            if (!(await manager.AddToRoleAsync(user, AppRoles.User)).Succeeded)
                return Results.BadRequest(new { error = "Unable to assign the application role." });
            roles = await manager.GetRolesAsync(user);
        }
        await transaction.CommitAsync(context.RequestAborted);
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
        return Results.Ok(sessions.Issue(user, roles));
    }

    private static string? ProfileName(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim()[..Math.Min(value.Trim().Length, 100)];
}
