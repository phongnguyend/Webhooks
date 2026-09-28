using System.Text.Json.Serialization;

namespace WebhookRouter.Models;

public sealed record ReceivedRequest(
    Guid Id,
    string TenantId,
    string TopicName,
    DateTimeOffset ReceivedAt,
    string Payload,
    [property: JsonIgnore] Guid CreatedByUserId);
