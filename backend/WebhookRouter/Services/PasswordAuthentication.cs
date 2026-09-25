using Microsoft.AspNetCore.Identity;
using WebhookRouter.Models;
using WebhookRouter.Data;

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
            context.Items[ActivityAudit.TargetKey] = user;
            if (user is null || !user.IsEnabled || !user.AllowPasswordAuthentication)
            {
                context.Items[ActivityAudit.ReasonKey] = user is null ? "UnknownAccount" : !user.IsEnabled ? "AccountDisabled" : "PasswordAuthenticationDisabled";
                return InvalidCredentials();
            }
            // No second-factor flow here: never bypass MFA when issuing a JWT.
            if (await manager.GetTwoFactorEnabledAsync(user)) { context.Items[ActivityAudit.ReasonKey] = "MfaRequired"; return InvalidCredentials(); }
            var wasLocked = await manager.IsLockedOutAsync(user);
            var result = await signIn.CheckPasswordSignInAsync(user, request.Password, lockoutOnFailure: true);
            if (!result.Succeeded)
            {
                context.Items[ActivityAudit.LockedKey] = !wasLocked && result.IsLockedOut;
                context.Items[ActivityAudit.ReasonKey] = result.IsLockedOut ? "AccountLockedOut" : "InvalidCredentials";
                return InvalidCredentials();
            }
            // Checks credentials/lockout/confirmation without issuing a cookie.
            return Results.Ok(sessions.Issue(user, await manager.GetRolesAsync(user)));
        }).AllowAnonymous().RequireRateLimiting("password-auth");

        app.MapPut("/api/auth/me/password", async (SetPasswordRequest request, HttpContext context,
            UserManager<AppUser> manager, SignInManager<AppUser> signIn, WebhookDbContext db) =>
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
                var wasLocked = await manager.IsLockedOutAsync(user);
                var check = await signIn.CheckPasswordSignInAsync(user, request.CurrentPassword, lockoutOnFailure: true);
                if (!check.Succeeded)
                {
                    ActivityAudit.Add(db, "LoginFailed", user, context.User, new { provider = "Password", reason = "CurrentPasswordRejected" });
                    if (!wasLocked && check.IsLockedOut) ActivityAudit.Add(db, "UserLockedOut", user, context.User, new { provider = "Password", reason = "FailedPasswordAttempts" });
                    await db.SaveChangesAsync();
                    return InvalidCredentials();
                }
            }
            // Password mutation and its audit record commit together.
            await using var transaction = await db.Database.BeginTransactionAsync();
            if (await manager.HasPasswordAsync(user))
            {
                result = await manager.ChangePasswordAsync(user, request.CurrentPassword!, request.NewPassword);
            }
            else result = await manager.AddPasswordAsync(user, request.NewPassword);
            if (result.Succeeded)
            {
                ActivityAudit.Add(db, "PasswordChanged", user, context.User, new { reason = "SelfService" });
                await db.SaveChangesAsync();
                await transaction.CommitAsync();
            }
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
