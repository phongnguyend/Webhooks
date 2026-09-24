namespace WebhookRouter.Contracts;

public sealed record UserProfileRequest(string? FirstName, string? LastName, string? PhoneNumber);
public sealed record ManageUserRequest(string Email, string? FirstName, string? LastName, string? PhoneNumber, bool IsEnabled, string[] Roles);

public sealed record TenantRequest(string Name, bool IsEnabled = true);
public sealed record AuditUserResponse(Guid Id, string? FirstName, string? LastName, string? Email);

public sealed record TenantResponse(
    Guid Id,
    string Name,
    bool IsEnabled,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    int TopicCount,
    AuditUserResponse? CreatedByUser,
    AuditUserResponse? UpdatedByUser);

public sealed record TopicRequest(
    string Key,
    string Name,
    bool IsEnabled,
    bool IsSharePointWebhook,
    bool UseManagedIdentity,
    string? FullyQualifiedNamespace,
    string? ServiceBusConnectionString,
    string ServiceBusEntityName,
    string ServiceBusEntityType = "Topic");

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
    string ServiceBusEntityType,
    string ServiceBusEntityName,
    DateTimeOffset CreatedAt,
    DateTimeOffset UpdatedAt,
    AuditUserResponse? CreatedByUser,
    AuditUserResponse? UpdatedByUser);

public sealed record EnabledRequest(bool IsEnabled);
