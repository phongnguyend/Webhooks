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

public static class WebhookEndpoints
{
    public static void MapWebhookEndpoints(this WebApplication app)
    {
        app.MapPost("/tenants/{tenantId:guid}/topics/{topicKey}", async (
            Guid tenantId, string topicKey, HttpRequest request, WebhookDbContext db, ServiceBusPublisher publisher,
            ConcurrentQueue<ReceivedRequest> requests, IHubContext<WebhookHub> hub, CancellationToken ct) =>
        {
            var normalizedTopic = NormalizeKey(topicKey);
            var topic = await db.Topics.AsNoTracking().Include(x => x.Tenant)
                .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Key == normalizedTopic, ct);
            if (topic is null)
            {
                return Results.NotFound(new
                {
                    error = "Webhook endpoint was not found."
                });
            }

            if (!topic.Tenant.IsEnabled || !topic.IsEnabled)
            {
                return Results.Json(new
                {
                    error = "This webhook endpoint is disabled."
                }, statusCode: StatusCodes.Status403Forbidden);
            }
            // SharePoint validation request: echo the token without publishing a message.
            if (topic.IsSharePointWebhook && request.Query.TryGetValue("validationtoken", out var token))
            {
                return Results.Text(token.ToString(), "text/plain");
            }

            using var reader = new StreamReader(request.Body);
            var body = await reader.ReadToEndAsync(ct);
            try
            {
                await publisher.PublishAsync(topic.UseManagedIdentity, topic.FullyQualifiedNamespace, topic.ServiceBusConnectionString,
                    topic.ServiceBusEntityName, body, topic.TenantId, topic.Key, request.ContentType, ct);
            }
            catch (Exception exception)
            {
                app.Logger.LogError(exception, "Failed to publish webhook for tenant {TenantId} and topic {TopicKey}", tenantId, topicKey);
                return Results.Problem("Azure Service Bus rejected or could not receive the message.", statusCode: StatusCodes.Status502BadGateway);
            }

            var received = new ReceivedRequest(Guid.NewGuid(), topic.TenantId.ToString(), topic.Key, DateTimeOffset.UtcNow, body, topic.Tenant.CreatedByUserId);
            requests.Enqueue(received);
            while (requests.Count > 500)
            {
                requests.TryDequeue(out _);
            }

            if (await db.Users.AnyAsync(x => x.Id == topic.Tenant.CreatedByUserId && x.IsEnabled, ct))
            {
                await hub.Clients.User(topic.Tenant.CreatedByUserId.ToString()).SendAsync("WebhookReceived", received, ct);
            }

            return Results.Accepted(value: received);
        });
    }
}
