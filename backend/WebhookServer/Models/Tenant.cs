namespace WebhookServer.Models;

public sealed class Tenant
{
    public Guid Id { get; set; }
    public Guid CreatedByUserId { get; set; }
    public required string Name { get; set; }
    public bool IsEnabled { get; set; } = true;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;
    public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    public ICollection<Topic> Topics { get; set; } = [];
    public AppUser CreatedByUser { get; set; } = null!;
}
