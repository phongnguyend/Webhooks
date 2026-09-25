using System.Security.Claims;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using WebhookRouter.Data;
using WebhookRouter.Models;

namespace WebhookRouter.Services;

public static class ActivityAudit
{
    public const string TargetKey = "Audit.LoginTarget";
    public const string ReasonKey = "Audit.LoginReason";
    public const string LockedKey = "Audit.NewLockout";
    public static readonly IReadOnlyDictionary<string, string[]> EventCategories = new Dictionary<string, string[]>
    {
        ["Authentication"] = ["LoginSucceeded", "LoginFailed", "UserLockedOut"],
        ["Users"] = ["PasswordChanged", "AccountEnabled", "AccountDisabled", "PasswordAuthenticationEnabled", "PasswordAuthenticationDisabled"],
        ["Tenants"] = ["TenantCreated", "TenantUpdated", "TenantEnabled", "TenantDisabled", "TenantDeleted"],
        ["Topics"] = ["TopicCreated", "TopicUpdated", "TopicEnabled", "TopicDisabled", "TopicDeleted"]
    };
    public static readonly string[] EventTypes = EventCategories.Values.SelectMany(events => events).ToArray();

    public static void Add(WebhookDbContext db, string eventType, AppUser target, ClaimsPrincipal? actor = null,
        object? metadata = null)
        => Add(db, eventType, "User", target.Id.ToString(), target.UserName, actor, metadata);

    public static void Add(WebhookDbContext db, string eventType, string entityType, string? entityId,
        string? entityName, ClaimsPrincipal? actor = null, object? metadata = null)
    {
        Guid? actorId = Guid.TryParse(actor?.FindFirstValue(ClaimTypes.NameIdentifier), out var id) ? id : null;
        db.ActivityLogs.Add(new ActivityLog
        {
            EventType = eventType, ActorUserId = actorId, ActorUsername = actor?.FindFirstValue("email"),
            EntityType = entityType, EntityId = entityId, EntityName = entityName,
            Metadata = JsonSerializer.Serialize(metadata ?? new { })
        });
    }

    public static void UseLoginAudit(this WebApplication app)
    {
        app.Use(async (context, next) =>
        {
            var provider = context.Request.Path.Value switch
            {
                "/api/auth/login" => "Password",
                "/api/auth/exchange/google" => "Google",
                "/api/auth/exchange/microsoft" => "Microsoft",
                _ => null
            };
            if (provider is null || !HttpMethods.IsPost(context.Request.Method)) { await next(); return; }
            // Execute before response headers are sent. Failure to persist prevents token delivery.
            context.Response.OnStarting(async () =>
            {
                await using var scope = app.Services.CreateAsyncScope();
                var db = scope.ServiceProvider.GetRequiredService<WebhookDbContext>();
                var target = context.Items[TargetKey] as AppUser;
                var succeeded = context.Response.StatusCode == StatusCodes.Status200OK && target is not null;
                db.ActivityLogs.Add(new ActivityLog
                {
                    EventType = succeeded ? "LoginSucceeded" : "LoginFailed",
                    EntityType = "User", EntityId = target?.Id.ToString(), EntityName = target?.UserName,
                    ActorUserId = succeeded ? target!.Id : null, ActorUsername = succeeded ? target!.UserName : null,
                    Metadata = JsonSerializer.Serialize(new
                    {
                        provider,
                        reason = succeeded ? null : context.Items[ReasonKey] as string
                            ?? (context.Response.StatusCode == 429 ? "RateLimited" : "AuthenticationRejected")
                    })
                });
                if (target is not null && context.Items[LockedKey] is true)
                    Add(db, "UserLockedOut", target, metadata: new { provider, reason = "FailedPasswordAttempts" });
                await db.SaveChangesAsync(CancellationToken.None);
            });
            await next();
        });
    }

    public static void MapActivityLog(this WebApplication app)
    {
        app.MapGet("/api/activity-logs", async (int? page, int? pageSize, string? eventType, string? search,
            string? entityType, string? entityId, Guid? userId, string? category, DateTimeOffset? from, DateTimeOffset? to,
            WebhookDbContext db, HttpContext context, CancellationToken ct) =>
        {
            var number = page ?? 1;
            var size = pageSize ?? 25;
            if (from.HasValue && to.HasValue && from.Value > to.Value)
                return Results.BadRequest(new { error = "From must be earlier than or equal to To." });
            if (number < 1 || number > 100000 || size < 1 || size > 100 || search?.Length > 256
                || entityType?.Length > 64 || entityId?.Length > 128
                || (!string.IsNullOrEmpty(category) && !EventCategories.ContainsKey(category))
                || (!string.IsNullOrEmpty(eventType) && !EventTypes.Contains(eventType)))
                return Results.BadRequest(new { error = "Invalid activity log filter or page." });
            var query = db.ActivityLogs.AsNoTracking();
            if (from.HasValue) query = query.Where(x => x.OccurredAt >= from.Value);
            if (to.HasValue) query = query.Where(x => x.OccurredAt <= to.Value);
            if (!string.IsNullOrEmpty(category))
            {
                var categoryEvents = EventCategories[category];
                query = query.Where(x => categoryEvents.Contains(x.EventType));
            }
            if (!string.IsNullOrEmpty(eventType)) query = query.Where(x => x.EventType == eventType);
            if (!string.IsNullOrWhiteSpace(entityType)) query = query.Where(x => x.EntityType == entityType);
            if (!string.IsNullOrWhiteSpace(entityId)) query = query.Where(x => x.EntityId == entityId);
            if (userId.HasValue)
            {
                var idText = userId.Value.ToString();
                query = query.Where(x => (x.EntityType == "User" && x.EntityId == idText) || x.ActorUserId == userId);
            }
            if (!string.IsNullOrWhiteSpace(search))
            {
                var term = search.Trim();
                var isId = Guid.TryParse(term, out var actorId);
                query = query.Where(x => (x.EntityName != null && x.EntityName.Contains(term))
                    || (x.ActorUsername != null && x.ActorUsername.Contains(term))
                    || x.EntityId == term || (isId && x.ActorUserId == actorId));
            }
            var total = await query.CountAsync(ct);
            var items = await query.OrderByDescending(x => x.OccurredAt).ThenByDescending(x => x.Id)
                .Skip((number - 1) * size).Take(size).ToListAsync(ct);
            context.Response.Headers.CacheControl = "no-store";
            return Results.Ok(new { items, total, page = number, pageSize = size });
        }).RequireAuthorization(AppRoles.ManageUsers);
    }
}
