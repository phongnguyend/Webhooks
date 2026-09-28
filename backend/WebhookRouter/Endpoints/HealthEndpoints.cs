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

public static class HealthEndpoints
{
    public static void MapHealthEndpoints(this WebApplication app)
    {
        app.MapGet("/healthz", () => Results.Ok(new { status = "Healthy" }));
    }
}
