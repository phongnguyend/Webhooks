namespace WebhookRouter.Models;

public sealed class Topic
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public Guid? CreatedByUserId { get; set; }
    public AppUser? CreatedByUser { get; set; }
    public Guid? UpdatedByUserId { get; set; }
    public AppUser? UpdatedByUser { get; set; }
    public required string Key { get; set; }
    public required string Name { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsSharePointWebhook { get; set; }
    public bool UseManagedIdentity { get; set; }
    public string? FullyQualifiedNamespace { get; set; }
    public string? ServiceBusConnectionString { get; set; }
    public string ServiceBusEntityType { get; set; } = "Topic";
    public required string ServiceBusEntityName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Tenant Tenant { get; set; } = null!;
}
