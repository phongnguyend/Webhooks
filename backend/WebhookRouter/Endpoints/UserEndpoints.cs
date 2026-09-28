using System.Collections.Concurrent;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WebhookRouter.Contracts;
using WebhookRouter.Data;
using WebhookRouter.Models;
using WebhookRouter.Services;
using static WebhookRouter.Endpoints.EndpointHelpers;

namespace WebhookRouter.Endpoints;

public static class UserEndpoints
{
    public static void MapUserEndpoints(this WebApplication app)
    {
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
                .Select(x => new
                {
                    x.Id,
                    username = x.UserName,
                    x.Email,
                    x.FirstName,
                    x.LastName,
                    x.PhoneNumber,
                    x.IsEnabled,
                    x.AllowPasswordAuthentication,
                    x.LockoutEnabled,
                    x.LockoutEnd,
                    x.AccessFailedCount,
                    HasPassword = x.PasswordHash != null,
                    HasExternalLogin = db.UserLogins.Any(login => login.UserId == x.Id)
                }).ToListAsync(ct);
            var memberships = await (from membership in db.UserRoles
                                     join role in db.Roles on membership.RoleId equals role.Id
                                     select new
                                     {
                                         membership.UserId,
                                         role.Name
                                     }).ToListAsync(ct);
            var rolesByUser = memberships.ToLookup(x => x.UserId, x => x.Name);
            return Results.Ok(accounts.Select(x => new
            {
                x.Id,
                x.username,
                x.Email,
                x.FirstName,
                x.LastName,
                x.PhoneNumber,
                x.IsEnabled,
                x.AllowPasswordAuthentication,
                x.LockoutEnabled,
                x.LockoutEnd,
                x.AccessFailedCount,
                x.HasPassword,
                x.HasExternalLogin,
                roles = rolesByUser[x.Id].ToArray()
            }));
        });
        users.MapPatch("/{userId:guid}/enabled", async (Guid userId, EnabledRequest request, ClaimsPrincipal principal, WebhookDbContext db, CancellationToken ct) =>
        {
            if (userId == GetUserId(principal) && !request.IsEnabled)
            {
                return Results.BadRequest(new
                {
                    error = "You cannot disable your own account."
                });
            }
            // Serialize administrator status changes to prevent disabling the last enabled admin concurrently.
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var account = await db.Users.SingleOrDefaultAsync(x => x.Id == userId, ct);
            if (account is null)
            {
                return Results.NotFound();
            }

            var adminIds = from membership in db.UserRoles
                           join role in db.Roles on membership.RoleId equals role.Id
                           where role.Name == AppRoles.GlobalAdmin
                           select membership.UserId;
            if (!request.IsEnabled && await adminIds.ContainsAsync(userId, ct)
                && !await db.Users.AnyAsync(x => x.Id != userId && x.IsEnabled && adminIds.Contains(x.Id), ct))
            {
                return Results.Conflict(new
                {
                    error = "At least one Global Admin must remain enabled."
                });
            }

            if (account.IsEnabled != request.IsEnabled)
            {
                ActivityAudit.Add(db, request.IsEnabled ? "AccountEnabled" : "AccountDisabled", account, principal);
            }

            account.IsEnabled = request.IsEnabled;
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.NoContent();
        });
    }
}
