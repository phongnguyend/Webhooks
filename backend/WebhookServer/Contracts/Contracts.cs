namespace WebhookServer.Contracts;

public sealed record UserProfileRequest(string? FirstName, string? LastName, string? PhoneNumber);

public sealed record TenantRequest(string Name, bool IsEnabled = true);

public sealed record TenantResponse(
    Guid Id,
    string Name,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int TopicCount);

public sealed record TopicRequest(
    string Key,
    string Name,
    bool IsEnabled,
    bool IsSharePointWebhook,
    bool UseManagedIdentity,
    string? FullyQualifiedNamespace,
    string? ServiceBusConnectionString,
    string ServiceBusTopicName);

public sealed record TopicResponse(
    Guid Id,
    Guid TenantId,
    string Key,
    string Name,
    bool IsEnabled,
    bool IsSharePointWebhook,
    bool UseManagedIdentity,
    string? FullyQualifiedNamespace,
    bool HasServiceBusConnection,
    string ServiceBusTopicName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt);

public sealed record EnabledRequest(bool IsEnabled);
