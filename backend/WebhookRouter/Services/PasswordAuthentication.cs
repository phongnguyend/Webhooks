using Microsoft.AspNetCore.Identity;
using WebhookRouter.Models;

namespace WebhookRouter.Services;

public static class PasswordAuthentication
{
    public static void MapPasswordSignIn(this WebApplication app)
    {
        app.MapPost("/api/auth/login", async (PasswordLoginRequest request, HttpContext context,
            UserManager<AppUser> manager, SignInManager<AppUser> signIn, JwtSessionService sessions) =>
        {
            NoCache(context);
            if (string.IsNullOrWhiteSpace(request.Username) || request.Username.Length > 256
                || string.IsNullOrEmpty(request.Password) || request.Password.Length > 1024)
                return InvalidCredentials();
            var user = await manager.FindByNameAsync(request.Username.Trim());
            if (user is null || !user.IsEnabled || !user.AllowPasswordAuthentication) return InvalidCredentials();
            // No second-factor flow here: never bypass MFA when issuing a JWT.
            if (await manager.GetTwoFactorEnabledAsync(user)) return InvalidCredentials();
            var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
            if (!result.Succeeded) return InvalidCredentials();
            // Checks credentials/lockout/confirmation without issuing a cookie.
            return Results.Ok(sessions.Issue(user, await manager.GetRolesAsync(user)));
        }).AllowAnonymous().RequireRateLimiting("password-auth");

        app.MapPut("/api/auth/me/password", async (SetPasswordRequest request, HttpContext context,
            UserManager<AppUser> manager, SignInManager<AppUser> signIn) =>
        {
            NoCache(context);
            var user = await manager.GetUserAsync(context.User);
            if (user is null || !user.IsEnabled) return Results.Unauthorized();
            if (!user.AllowPasswordAuthentication || await manager.GetTwoFactorEnabledAsync(user)) return Results.Forbid();
            if (string.IsNullOrEmpty(request.NewPassword) || request.NewPassword.Length > 1024)
                return Results.BadRequest(new { error = "A new password of at most 1024 characters is required." });
            IdentityResult result;
            if (await manager.HasPasswordAsync(user))
            {
                if (string.IsNullOrEmpty(request.CurrentPassword) || request.CurrentPassword.Length > 1024)
                    return InvalidCredentials();
                var check = await signIn.CheckPasswordSignInAsync(user, request.CurrentPassword, lockoutOnFailure: true);
                if (!check.Succeeded) return InvalidCredentials();
                result = await manager.ChangePasswordAsync(user, request.CurrentPassword, request.NewPassword);
            }
            else result = await manager.AddPasswordAsync(user, request.NewPassword);
            // Identity rotates the security stamp, invalidating existing JWTs.
            return result.Succeeded ? Results.NoContent()
                : Results.BadRequest(new { error = string.Join(" ", result.Errors.Select(x => x.Description)) });
        }).RequireAuthorization().RequireRateLimiting("password-auth");
    }

    private static IResult InvalidCredentials() => Results.Json(
        new { error = "Invalid username or password." }, statusCode: StatusCodes.Status401Unauthorized);

    private static void NoCache(HttpContext context)
    {
        context.Response.Headers.CacheControl = "no-store";
        context.Response.Headers.Pragma = "no-cache";
    }

    public sealed record PasswordLoginRequest(string? Username, string? Password);
    public sealed record SetPasswordRequest(string? CurrentPassword, string? NewPassword);
}

// Administrator approval permits password login without falsely marking the email verified.
public sealed class PasswordAccountConfirmation : IUserConfirmation<AppUser>
{
    public Task<bool> IsConfirmedAsync(UserManager<AppUser> manager, AppUser user) =>
        Task.FromResult(user.EmailConfirmed || user.AllowPasswordAuthentication);
}
