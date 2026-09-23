namespace WebhookServer.Models;

public sealed class Topic
{
    public Guid Id { get; set; }
    public Guid TenantId { get; set; }
    public required string Key { get; set; }
    public required string Name { get; set; }
    public bool IsEnabled { get; set; } = true;
    public bool IsSharePointWebhook { get; set; }
    public required string ServiceBusTopicName { get; set; }
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public Tenant Tenant { get; set; } = null!;
}
