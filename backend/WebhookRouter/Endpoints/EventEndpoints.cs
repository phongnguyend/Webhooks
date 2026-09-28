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

public static class EventEndpoints
{
    public static void MapEventEndpoints(this WebApplication app)
    {
        app.MapGet("/", (ClaimsPrincipal user, ConcurrentQueue<ReceivedRequest> requests) =>
            requests.Where(x => x.CreatedByUserId == GetUserId(user)).Reverse().Take(500)).RequireAuthorization();
        app.MapPost("/reset", (ClaimsPrincipal user, ConcurrentQueue<ReceivedRequest> requests) =>
        {
            var userId = GetUserId(user);
            var retained = requests.Where(x => x.CreatedByUserId != userId).ToArray();
            requests.Clear();
            foreach (var item in retained)
            {
                requests.Enqueue(item);
            }

            return Results.NoContent();
        }).RequireAuthorization();

        app.MapHub<WebhookHub>("/hubs/events", options => options.CloseOnAuthenticationExpiration = true).RequireAuthorization();
    }
}
