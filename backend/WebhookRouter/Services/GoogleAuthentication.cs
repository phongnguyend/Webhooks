using System.Security.Claims;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.IdentityModel.Tokens;
using WebhookRouter.Models;

namespace WebhookRouter.Services;

public static class GoogleAuthentication
{
    private const string Scheme = "GoogleExchange";
    public static void AddGoogleSignIn(this WebApplicationBuilder builder)
    {
        var googleClientId = builder.Configuration["Authentication:Google:ClientId"] ?? string.Empty;
        builder.Services.AddAuthentication()
        .AddJwtBearer(Scheme, options =>
        {
            options.Authority = "https://accounts.google.com";
            options.Audience = googleClientId;
            options.MapInboundClaims = false;
            options.TokenValidationParameters.NameClaimType = "email";
            options.TokenValidationParameters.ValidIssuers = ["https://accounts.google.com", "accounts.google.com"];
            options.TokenValidationParameters.ValidAlgorithms = [SecurityAlgorithms.RsaSha256];
            options.TokenValidationParameters.ClockSkew = TimeSpan.FromSeconds(30);
            options.Events = new JwtBearerEvents
            {
                OnMessageReceived = context =>
                {
                    if (context.Request.Path != "/api/auth/exchange/google" || !HttpMethods.IsPost(context.Request.Method))
                        context.NoResult();
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
                    var user = await userManager.FindByLoginAsync("Google", subject);
                    if (user is null)
                    {
                        user = await userManager.FindByEmailAsync(email);
                        // Only unlinked preconfigured accounts can be claimed by verified Google email.
                        // Never merge a Microsoft-created account by email (account pre-hijacking risk).
                        if (user is not null && (await userManager.GetLoginsAsync(user)).Count > 0)
                        {
                            context.Fail("This account already uses another external identity. Sign in with its connected provider.");
                            return;
                        }
                    }
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
    
                    context.HttpContext.Items[ActivityAudit.TargetKey] = user;
                    if (!user.IsEnabled)
                    {
                        context.Fail("This account is disabled.");
                        return;
                    }
    
                    var logins = await userManager.GetLoginsAsync(user);
                    if (logins.Any(x => x.LoginProvider == "Google" && x.ProviderKey != subject))
                    {
                        context.Fail("This application account is linked to a different Google account.");
                        return;
                    }
                    if (logins.All(x => x.LoginProvider != "Google" || x.ProviderKey != subject))
                    {
                        var loginResult = await userManager.AddLoginAsync(user, new UserLoginInfo("Google", subject, "Google"));
                        if (!loginResult.Succeeded) { context.Fail("Unable to link the Google account."); return; }
                    }
    
                    if (!user.EmailConfirmed && string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase))
                    {
                        user.EmailConfirmed = true;
                        var confirmed = await userManager.UpdateAsync(user);
                        if (!confirmed.Succeeded) { context.Fail("Unable to confirm the application email."); return; }
                    }
    
                    var roles = await userManager.GetRolesAsync(user);
                    if (roles.Count == 0)
                    {
                        var result = await userManager.AddToRoleAsync(user, AppRoles.User);
                        if (!result.Succeeded) { context.Fail("Unable to assign the application role."); return; }
                        roles = await userManager.GetRolesAsync(user);
                    }
    
                    if (context.Principal?.Identity is ClaimsIdentity identity)
                    {
                        foreach (var claim in identity.FindAll(ClaimTypes.NameIdentifier).Concat(identity.FindAll(identity.RoleClaimType)).ToArray())
                            identity.RemoveClaim(claim);
                        identity.AddClaim(new Claim(ClaimTypes.NameIdentifier, user.Id.ToString()));
                        foreach (var role in roles) identity.AddClaim(new Claim(identity.RoleClaimType, role));
                    }
                }
            };
        });
    }

    public static void MapGoogleSignIn(this WebApplication app)
    {
        app.MapPost("/api/auth/exchange/google", async (HttpContext context, UserManager<AppUser> manager, JwtSessionService sessions) =>
        {
            var user = await manager.GetUserAsync(context.User);
            if (user is null || !user.IsEnabled) return Results.Unauthorized();
            context.Response.Headers.CacheControl = "no-store";
            context.Response.Headers.Pragma = "no-cache";
            return Results.Ok(sessions.Issue(user, await manager.GetRolesAsync(user)));
        }).RequireAuthorization(policy => policy.AddAuthenticationSchemes(Scheme).RequireAuthenticatedUser());
    }

    private static string? NormalizeProfileName(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
}
