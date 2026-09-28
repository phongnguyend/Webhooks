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

public static class ProfileEndpoints
{
    public static void MapProfileEndpoints(this WebApplication app)
    {
        app.MapGet("/api/auth/me", async (ClaimsPrincipal principal, UserManager<AppUser> userManager) =>
        {
            if (principal.Identity?.IsAuthenticated != true)
            {
                return Results.Unauthorized();
            }

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
            {
                return Results.BadRequest(new
                {
                    error = "First name and last name must each be at most 100 characters."
                });
            }

            if (phoneNumber?.Length > 50)
            {
                return Results.BadRequest(new
                {
                    error = "Phone number must be at most 50 characters."
                });
            }

            var user = await userManager.GetUserAsync(principal);
            if (user is null)
            {
                return Results.Unauthorized();
            }

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
                : Results.BadRequest(new
                {
                    error = string.Join(" ", result.Errors.Select(x => x.Description))
                });
        }).RequireAuthorization();
    }
}
