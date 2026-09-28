using System.Security.Claims;
using System.Text.RegularExpressions;
using WebhookRouter.Contracts;
using WebhookRouter.Models;

namespace WebhookRouter.Endpoints;

internal static class EndpointHelpers
{
    internal static IResult? ValidateTenant(TenantRequest request)
    {
        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
        {
            return Results.BadRequest(new
            {
                error = "Tenant name is required and must be at most 200 characters."
            });
        }

        return null;
    }

    internal static IResult? ValidateTopic(TopicRequest request, bool isCreate, bool hasStoredConnection)
    {
        if (!ValidKey(request.Key))
        {
            return Results.BadRequest(new
            {
                error = "Topic key must be 1-100 letters, numbers, or hyphens."
            });
        }

        if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200)
        {
            return Results.BadRequest(new
            {
                error = "Topic name is required and must be at most 200 characters."
            });
        }

        if (!ValidServiceBusEntityType(request.ServiceBusEntityType))
        {
            return Results.BadRequest(new
            {
                error = "Azure Service Bus destination type must be Topic or Queue."
            });
        }

        if (string.IsNullOrWhiteSpace(request.ServiceBusEntityName) || request.ServiceBusEntityName.Trim().Length > 260)
        {
            return Results.BadRequest(new
            {
                error = "Azure Service Bus entity name is required and must be at most 260 characters."
            });
        }

        if (request.UseManagedIdentity && string.IsNullOrWhiteSpace(request.FullyQualifiedNamespace))
        {
            return Results.BadRequest(new
            {
                error = "Fully qualified namespace is required for managed identity."
            });
        }

        if (request.UseManagedIdentity && request.FullyQualifiedNamespace!.Trim().Length > 300)
        {
            return Results.BadRequest(new
            {
                error = "Fully qualified namespace must be at most 300 characters."
            });
        }

        if (!request.UseManagedIdentity && string.IsNullOrWhiteSpace(request.ServiceBusConnectionString) && (isCreate || !hasStoredConnection))
        {
            return Results.BadRequest(new
            {
                error = "Connection string is required when managed identity is disabled."
            });
        }

        if (!string.IsNullOrWhiteSpace(request.ServiceBusConnectionString) && request.ServiceBusConnectionString.Trim().Length > 2000)
        {
            return Results.BadRequest(new
            {
                error = "Connection string must be at most 2000 characters."
            });
        }

        return null;
    }

    internal static bool ValidKey(string? key) => !string.IsNullOrWhiteSpace(key) && key.Trim().Length <= 100 && Regex.IsMatch(key.Trim(), "^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.IgnoreCase);
    internal static bool ValidServiceBusEntityType(string? value) => string.Equals(value, "Topic", StringComparison.OrdinalIgnoreCase) || string.Equals(value, "Queue", StringComparison.OrdinalIgnoreCase);
    internal static string NormalizeKey(string value) => value.Trim().ToLowerInvariant();
    internal static string NormalizeNamespace(string value) => value.Trim().Replace("https://", "", StringComparison.OrdinalIgnoreCase).TrimEnd('/');
    internal static string NormalizeServiceBusEntityType(string value) => string.Equals(value, "Queue", StringComparison.OrdinalIgnoreCase) ? "Queue" : "Topic";
    internal static string? NormalizeProfileName(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    internal static Guid GetUserId(ClaimsPrincipal user) => Guid.Parse(user.FindFirstValue(ClaimTypes.NameIdentifier)!);
    internal static object ToUserResponse(AppUser user, IList<string> roles) => new
    {
        id = user.Id,
        username = user.UserName,
        email = user.Email,
        firstName = user.FirstName,
        lastName = user.LastName,
        phoneNumber = user.PhoneNumber,
        isEnabled = user.IsEnabled,
        roles
    };
    internal static AuditUserResponse? ToAuditUser(AppUser? user) => user is null ? null : new(user.Id, user.FirstName, user.LastName, user.Email);
    internal static TenantResponse ToResponse(Tenant tenant, int count) => new(tenant.Id, tenant.Name, tenant.IsEnabled, tenant.CreatedAt, tenant.UpdatedAt, count, ToAuditUser(tenant.CreatedByUser), ToAuditUser(tenant.UpdatedByUser));
    internal static TopicResponse ToTopicResponse(Topic topic) => new(topic.Id, topic.TenantId, topic.Key, topic.Name, topic.IsEnabled,
        topic.IsSharePointWebhook, topic.UseManagedIdentity, topic.FullyQualifiedNamespace, !string.IsNullOrWhiteSpace(topic.ServiceBusConnectionString),
        topic.ServiceBusEntityType, topic.ServiceBusEntityName, topic.CreatedAt, topic.UpdatedAt, ToAuditUser(topic.CreatedByUser), ToAuditUser(topic.UpdatedByUser));
}
