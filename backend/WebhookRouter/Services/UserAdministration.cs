using System.ComponentModel.DataAnnotations;
using System.Data;
using System.Security.Claims;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using WebhookRouter.Contracts;
using WebhookRouter.Data;
using WebhookRouter.Models;

namespace WebhookRouter.Services;

public static class UserAdministration
{
    public static async Task<IResult> SaveAsync(Guid? id, ManageUserRequest request, ClaimsPrincipal principal,
        UserManager<AppUser> manager, WebhookDbContext db, CancellationToken ct)
    {
        var email = request.Email?.Trim().ToLowerInvariant();
        var firstName = Clean(request.FirstName);
        var lastName = Clean(request.LastName);
        var phone = Clean(request.PhoneNumber);
        var roles = request.Roles?.Distinct(StringComparer.Ordinal).ToArray() ?? [];
        if (string.IsNullOrEmpty(email) || email.Length > 256 || !new EmailAddressAttribute().IsValid(email))
            return Error("A valid email of at most 256 characters is required.");
        if (firstName?.Length > 100 || lastName?.Length > 100 || phone?.Length > 50)
            return Error("Names must be at most 100 characters and phone at most 50 characters.");
        if (roles.Length == 0 || roles.Any(role => role != AppRoles.User && role != AppRoles.GlobalAdmin))
            return Error("Select at least one supported role: User or Global Admin.");

        // Shares serializable locking with the enable/disable endpoint.
        await using var transaction = await db.Database.BeginTransactionAsync(IsolationLevel.Serializable, ct);
        var account = id.HasValue ? await manager.FindByIdAsync(id.Value.ToString()) : new AppUser();
        if (account is null) return Results.NotFound();
        var existingRoles = id.HasValue ? await manager.GetRolesAsync(account) : [];
        var currentUserId = principal.FindFirstValue(ClaimTypes.NameIdentifier);
        if (id?.ToString() == currentUserId && (!request.IsEnabled || !roles.Contains(AppRoles.GlobalAdmin)))
            return Error("You cannot disable yourself or remove your own Global Admin role.");
        if (id.HasValue && existingRoles.Contains(AppRoles.GlobalAdmin)
            && (!request.IsEnabled || !roles.Contains(AppRoles.GlobalAdmin)))
        {
            var adminIds = from membership in db.UserRoles join role in db.Roles on membership.RoleId equals role.Id
                           where role.Name == AppRoles.GlobalAdmin select membership.UserId;
            if (!await db.Users.AnyAsync(user => user.Id != id.Value && user.IsEnabled && adminIds.Contains(user.Id), ct))
                return Results.Conflict(new { error = "At least one Global Admin must remain enabled." });
        }

        var emailChanged = !string.Equals(account.Email, email, StringComparison.OrdinalIgnoreCase);
        if (id.HasValue && emailChanged)
            return Error("Email cannot be changed after the user is created.");
        var normalizedEmail = manager.NormalizeEmail(email);
        if (await db.Users.AnyAsync(user => user.NormalizedEmail == normalizedEmail && (!id.HasValue || user.Id != id.Value), ct))
            return Results.Conflict(new { error = "A user with this email already exists." });

        if (!id.HasValue)
        {
            account.Email = email;
            account.UserName = email;
        }
        account.FirstName = firstName;
        account.LastName = lastName;
        if (account.PhoneNumber != phone) account.PhoneNumberConfirmed = false;
        account.PhoneNumber = phone;
        account.IsEnabled = request.IsEnabled;
        if (emailChanged) account.EmailConfirmed = false;
        var result = id.HasValue ? await manager.UpdateAsync(account) : await manager.CreateAsync(account);
        if (!result.Succeeded) return Failure(result);
        var removed = existingRoles.Except(roles).ToArray();
        var added = roles.Except(existingRoles).ToArray();
        if (removed.Length > 0)
        {
            result = await manager.RemoveFromRolesAsync(account, removed);
            if (!result.Succeeded) return Failure(result);
        }
        if (added.Length > 0)
        {
            result = await manager.AddToRolesAsync(account, added);
            if (!result.Succeeded) return Failure(result);
        }
        await transaction.CommitAsync(ct);
        return id.HasValue ? Results.NoContent() : Results.Created($"/api/users/{account.Id}", new { account.Id });
    }

    private static string? Clean(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    private static IResult Error(string message) => Results.BadRequest(new { error = message });
    private static IResult Failure(IdentityResult result) => Error(string.Join(" ", result.Errors.Select(error => error.Description)));
}
