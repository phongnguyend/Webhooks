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

public static class TenantEndpoints
{
    public static void MapTenantEndpoints(this WebApplication app)
    {
        var tenants = app.MapGroup("/api/tenants").RequireAuthorization();

        tenants.MapGet("/", async (ClaimsPrincipal user, WebhookDbContext db, CancellationToken ct) =>
        {
            var userId = GetUserId(user);
            var isGlobalAdmin = user.IsInRole(AppRoles.GlobalAdmin);
            return await db.Tenants.AsNoTracking().Where(x => isGlobalAdmin || x.CreatedByUserId == userId).OrderBy(x => x.Name)
                .Select(x => new TenantResponse(x.Id, x.Name, x.IsEnabled, x.CreatedAt, x.UpdatedAt, x.Topics.Count,
                    new AuditUserResponse(x.CreatedByUser.Id, x.CreatedByUser.FirstName, x.CreatedByUser.LastName, x.CreatedByUser.Email),
                    x.UpdatedByUser == null ? null : new AuditUserResponse(x.UpdatedByUser.Id, x.UpdatedByUser.FirstName, x.UpdatedByUser.LastName, x.UpdatedByUser.Email)))
                .ToListAsync(ct);
        });

        tenants.MapPost("/", async (TenantRequest request, ClaimsPrincipal user, WebhookDbContext db, CancellationToken ct) =>
        {
            var error = ValidateTenant(request);
            if (error is not null)
            {
                return error;
            }

            var tenant = new Tenant { CreatedByUserId = GetUserId(user), UpdatedByUserId = GetUserId(user), Name = request.Name.Trim(), IsEnabled = request.IsEnabled };
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            db.Tenants.Add(tenant);
            await db.SaveChangesAsync(ct);
            RouteActivityAudit.Add(db, "Created", tenant, user, new
            {
                after = RouteActivityAudit.State(tenant)
            });
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            await db.Entry(tenant).Reference(x => x.CreatedByUser).LoadAsync(ct);
            await db.Entry(tenant).Reference(x => x.UpdatedByUser).LoadAsync(ct);
            return Results.Created($"/api/tenants/{tenant.Id}", ToResponse(tenant, 0));
        });

        tenants.MapPut("/{tenantId:guid}", async (Guid tenantId, TenantRequest request, ClaimsPrincipal user, WebhookDbContext db, CancellationToken ct) =>
        {
            var error = ValidateTenant(request);
            if (error is not null)
            {
                return error;
            }

            var userId = GetUserId(user);
            var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Id == tenantId && x.CreatedByUserId == userId, ct);
            if (tenant is null)
            {
                return Results.NotFound();
            }

            var before = RouteActivityAudit.State(tenant);
            var wasEnabled = tenant.IsEnabled;
            var nameChanged = tenant.Name != request.Name.Trim();
            tenant.Name = request.Name.Trim();
            tenant.IsEnabled = request.IsEnabled;
            tenant.UpdatedByUserId = userId;
            tenant.UpdatedAt = DateTimeOffset.UtcNow;
            if (nameChanged)
            {
                RouteActivityAudit.Add(db, "Updated", tenant, user, new
                {
                    before,
                    after = RouteActivityAudit.State(tenant)
                });
            }

            if (wasEnabled != tenant.IsEnabled)
            {
                RouteActivityAudit.Add(db, tenant.IsEnabled ? "Enabled" : "Disabled", tenant, user, new
                {
                    before,
                    after = RouteActivityAudit.State(tenant)
                });
            }

            await db.SaveChangesAsync(ct);
            await db.Entry(tenant).Reference(x => x.CreatedByUser).LoadAsync(ct);
            await db.Entry(tenant).Reference(x => x.UpdatedByUser).LoadAsync(ct);
            return Results.Ok(ToResponse(tenant, await db.Topics.CountAsync(x => x.TenantId == tenantId, ct)));
        });

        tenants.MapPatch("/{tenantId:guid}/enabled", async (Guid tenantId, EnabledRequest request, ClaimsPrincipal user, WebhookDbContext db, CancellationToken ct) =>
        {
            var userId = GetUserId(user);
            var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Id == tenantId && x.CreatedByUserId == userId, ct);
            if (tenant is null)
            {
                return Results.NotFound();
            }

            if (tenant.IsEnabled == request.IsEnabled)
            {
                return Results.NoContent();
            }

            RouteActivityAudit.Add(db, request.IsEnabled ? "Enabled" : "Disabled", tenant, user,
                new
                {
                    before = tenant.IsEnabled,
                    after = request.IsEnabled
                });
            tenant.IsEnabled = request.IsEnabled;
            tenant.UpdatedAt = DateTimeOffset.UtcNow;
            tenant.UpdatedByUserId = userId;
            await db.SaveChangesAsync(ct);
            return Results.NoContent();
        });

        tenants.MapDelete("/{tenantId:guid}", async (Guid tenantId, ClaimsPrincipal user, WebhookDbContext db, CancellationToken ct) =>
        {
            var userId = GetUserId(user);
            await using var transaction = await db.Database.BeginTransactionAsync(System.Data.IsolationLevel.Serializable, ct);
            var tenant = await db.Tenants.SingleOrDefaultAsync(x => x.Id == tenantId && x.CreatedByUserId == userId, ct);
            if (tenant is null)
            {
                return Results.NotFound();
            }

            var deletedTopics = await db.Topics.Where(x => x.TenantId == tenantId).ToListAsync(ct);
            foreach (var deletedTopic in deletedTopics)
            {
                RouteActivityAudit.Add(db, "Deleted", deletedTopic, user, new
                {
                    reason = "TenantDeleted",
                    before = RouteActivityAudit.State(deletedTopic)
                });
            }

            RouteActivityAudit.Add(db, "Deleted", tenant, user, new
            {
                before = RouteActivityAudit.State(tenant),
                deletedTopicCount = deletedTopics.Count
            });
            db.Tenants.Remove(tenant);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            return Results.NoContent();
        });
    }
}
