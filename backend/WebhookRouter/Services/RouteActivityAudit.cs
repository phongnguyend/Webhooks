using System.Security.Claims;
using WebhookRouter.Data;
using WebhookRouter.Models;

namespace WebhookRouter.Services;

public static class RouteActivityAudit
{
    // Explicit allowlist: never serialize an entity or its Service Bus credentials.
    public static object State(Tenant tenant) => new { name = tenant.Name, isEnabled = tenant.IsEnabled };
    public static object State(Topic topic) => new
    {
        tenantId = topic.TenantId, key = topic.Key, name = topic.Name, isEnabled = topic.IsEnabled,
        isSharePointWebhook = topic.IsSharePointWebhook, useManagedIdentity = topic.UseManagedIdentity,
        fullyQualifiedNamespace = topic.FullyQualifiedNamespace,
        serviceBusEntityType = topic.ServiceBusEntityType, serviceBusEntityName = topic.ServiceBusEntityName
    };

    public static void Add(WebhookDbContext db, string action, Tenant tenant, ClaimsPrincipal actor, object metadata)
        => ActivityAudit.Add(db, $"Tenant{action}", "Tenant", tenant.Id.ToString(), tenant.Name, actor, metadata);

    public static void Add(WebhookDbContext db, string action, Topic topic, ClaimsPrincipal actor, object metadata)
        => ActivityAudit.Add(db, $"Topic{action}", "Topic", topic.Id.ToString(), topic.Name, actor,
            new { tenantId = topic.TenantId, details = metadata });
}
