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

public static class TopicEndpoints
{
    public static void MapTopicEndpoints(this WebApplication app)
    {
        var tenants = app.MapGroup("/api/tenants").RequireAuthorization();

        tenants.MapGet("/{tenantId:guid}/topics", async (Guid tenantId, ClaimsPrincipal user, WebhookDbContext db, CancellationToken ct) =>
        {
            var userId = GetUserId(user);
            var isGlobalAdmin = user.IsInRole(AppRoles.GlobalAdmin);
            if (!await db.Tenants.AnyAsync(x => x.Id == tenantId && (isGlobalAdmin || x.CreatedByUserId == userId), ct))
            {
                return Results.NotFound();
            }

            var topics = await db.Topics.AsNoTracking().Include(x => x.CreatedByUser).Include(x => x.UpdatedByUser)
                .Where(x => x.TenantId == tenantId).OrderBy(x => x.Name).ToListAsync(ct);
            return Results.Ok(topics.Select(ToTopicResponse));
        });

        tenants.MapPost("/{tenantId:guid}/topics", async (Guid tenantId, TopicRequest request, ClaimsPrincipal user, WebhookDbContext db, CancellationToken ct) =>
        {
            var error = ValidateTopic(request, isCreate: true, hasStoredConnection: false);
            if (error is not null)
            {
                return error;
            }

            var userId = GetUserId(user);
            if (!await db.Tenants.AnyAsync(x => x.Id == tenantId && x.CreatedByUserId == userId, ct))
            {
                return Results.NotFound();
            }

            var key = NormalizeKey(request.Key);
            if (await db.Topics.AnyAsync(x => x.TenantId == tenantId && x.Key == key, ct))
            {
                return Results.Conflict(new
                {
                    error = "A topic with this key already exists in the tenant."
                });
            }

            var topic = new Topic
            {
                TenantId = tenantId,
                Key = key,
                Name = request.Name.Trim(),
                IsEnabled = request.IsEnabled,
                CreatedByUserId = userId,
                UpdatedByUserId = userId,
                IsSharePointWebhook = request.IsSharePointWebhook,
                UseManagedIdentity = request.UseManagedIdentity,
                FullyQualifiedNamespace = request.UseManagedIdentity ? NormalizeNamespace(request.FullyQualifiedNamespace!) : null,
                ServiceBusConnectionString = request.UseManagedIdentity ? null : request.ServiceBusConnectionString!.Trim(),
                ServiceBusEntityType = NormalizeServiceBusEntityType(request.ServiceBusEntityType),
                ServiceBusEntityName = request.ServiceBusEntityName.Trim()
            };
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            db.Topics.Add(topic);
            await db.SaveChangesAsync(ct);
            RouteActivityAudit.Add(db, "Created", topic, user, new
            {
                after = RouteActivityAudit.State(topic)
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await db.Entry(topic).Reference(x => x.CreatedByUser).LoadAsync(ct);
            await db.Entry(topic).Reference(x => x.UpdatedByUser).LoadAsync(ct);
            return Results.Created($"/api/tenants/{tenantId}/topics/{topic.Id}", ToTopicResponse(topic));
        });

        tenants.MapPut("/{tenantId:guid}/topics/{topicId:guid}", async (Guid tenantId, Guid topicId, TopicRequest request, ClaimsPrincipal user, WebhookDbContext db, CancellationToken ct) =>
        {
            var userId = GetUserId(user);
            var topic = await db.Topics.SingleOrDefaultAsync(x => x.Id == topicId && x.TenantId == tenantId && x.Tenant.CreatedByUserId == userId, ct);
            if (topic is null)
            {
                return Results.NotFound();
            }

            var error = ValidateTopic(request, isCreate: false, hasStoredConnection: !string.IsNullOrWhiteSpace(topic.ServiceBusConnectionString));
            if (error is not null)
            {
                return error;
            }

            var key = NormalizeKey(request.Key);
            if (await db.Topics.AnyAsync(x => x.TenantId == tenantId && x.Id != topicId && x.Key == key, ct))
            {
                return Results.Conflict(new
                {
                    error = "A topic with this key already exists in the tenant."
                });
            }

            var before = RouteActivityAudit.State(topic);
            var wasEnabled = topic.IsEnabled;
            var previousConnectionString = topic.ServiceBusConnectionString;
            topic.Key = key;
            topic.UpdatedByUserId = userId;
            topic.Name = request.Name.Trim();
            topic.IsEnabled = request.IsEnabled;
            topic.IsSharePointWebhook = request.IsSharePointWebhook;
            topic.UseManagedIdentity = request.UseManagedIdentity;
            topic.FullyQualifiedNamespace = request.UseManagedIdentity ? NormalizeNamespace(request.FullyQualifiedNamespace!) : null;
            topic.ServiceBusEntityType = NormalizeServiceBusEntityType(request.ServiceBusEntityType);
            topic.ServiceBusEntityName = request.ServiceBusEntityName.Trim();
            if (request.UseManagedIdentity)
            {
                topic.ServiceBusConnectionString = null;
            }
            else if (!string.IsNullOrWhiteSpace(request.ServiceBusConnectionString))
            {
                topic.ServiceBusConnectionString = request.ServiceBusConnectionString.Trim();
            }

            topic.UpdatedAt = DateTimeOffset.UtcNow;
            var connectionStringChanged = previousConnectionString != topic.ServiceBusConnectionString;
            var after = RouteActivityAudit.State(topic);
            if (!before.Equals(after) || connectionStringChanged)
            {
                RouteActivityAudit.Add(db, "Updated", topic, user, new
                {
                    before,
                    after,
                    connectionStringChanged
                });
            }

            if (wasEnabled != topic.IsEnabled)
            {
                RouteActivityAudit.Add(db, topic.IsEnabled ? "Enabled" : "Disabled", topic, user, new
                {
                    before = wasEnabled,
                    after = topic.IsEnabled
                });
            }

            await db.SaveChangesAsync(ct);
            await db.Entry(topic).Reference(x => x.CreatedByUser).LoadAsync(ct);
            await db.Entry(topic).Reference(x => x.UpdatedByUser).LoadAsync(ct);
            return Results.Ok(ToTopicResponse(topic));
        });

        tenants.MapPatch("/{tenantId:guid}/topics/{topicId:guid}/enabled", async (Guid tenantId, Guid topicId, EnabledRequest request, ClaimsPrincipal user, WebhookDbContext db, CancellationToken ct) =>
        {
            var userId = GetUserId(user);
            var topic = await db.Topics.SingleOrDefaultAsync(x => x.Id == topicId && x.TenantId == tenantId && x.Tenant.CreatedByUserId == userId, ct);
            if (topic is null)
            {
                return Results.NotFound();
            }

            if (topic.IsEnabled == request.IsEnabled)
            {
                return Results.NoContent();
            }

            RouteActivityAudit.Add(db, request.IsEnabled ? "Enabled" : "Disabled", topic, user,
                new
                {
                    before = topic.IsEnabled,
                    after = request.IsEnabled
                });
            topic.IsEnabled = request.IsEnabled;
            topic.UpdatedAt = DateTimeOffset.UtcNow;
            topic.UpdatedByUserId = userId;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        tenants.MapDelete("/{tenantId:guid}/topics/{topicId:guid}", async (Guid tenantId, Guid topicId, ClaimsPrincipal user, WebhookDbContext db, CancellationToken ct) =>
        {
            var userId = GetUserId(user);
            var topic = await db.Topics.SingleOrDefaultAsync(x => x.Id == topicId && x.TenantId == tenantId && x.Tenant.CreatedByUserId == userId, ct);
            if (topic is null)
            {
                return Results.NotFound();
            }

            RouteActivityAudit.Add(db, "Deleted", topic, user, new
            {
                before = RouteActivityAudit.State(topic)
            });
            db.Topics.Remove(topic);
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });
    }
}
