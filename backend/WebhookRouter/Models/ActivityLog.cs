namespace WebhookRouter.Models;

public sealed class ActivityLog
{
    public Guid Id { get; set; }
    public DateTimeOffset OccurredAt { get; set; } = DateTimeOffset.UtcNow;
    public required string EventType { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? ActorUsername { get; set; }
    public required string EntityType { get; set; }
    public string? EntityId { get; set; }
    public string? EntityName { get; set; }
    public string Metadata { get; set; } = "{}";
}
