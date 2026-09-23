using Azure.Identity;
using Azure.Messaging.ServiceBus;

namespace WebhookServer.Services;

public sealed class ServiceBusOptions
{
    public const string SectionName = "ServiceBus";
    public bool UseManagedIdentity { get; set; }
    public string? FullyQualifiedNamespace { get; set; }
    public string? ConnectionString { get; set; }
}

public sealed class ServiceBusPublisher : IAsyncDisposable
{
    private readonly ServiceBusClient _client;

    public ServiceBusPublisher(ServiceBusOptions options)
    {
        _client = options.UseManagedIdentity
            ? new ServiceBusClient(NormalizeNamespace(options.FullyQualifiedNamespace
                ?? throw new InvalidOperationException("ServiceBus:FullyQualifiedNamespace is required when managed identity is enabled.")), new DefaultAzureCredential())
            : new ServiceBusClient(options.ConnectionString
                ?? throw new InvalidOperationException("ServiceBus:ConnectionString is required when managed identity is disabled."));
    }

    public async Task PublishAsync(
        string serviceBusTopicName,
        string body,
        Guid tenantId,
        string topicKey,
        string? contentType,
        CancellationToken cancellationToken)
    {
        await using var sender = _client.CreateSender(serviceBusTopicName);
        var message = new ServiceBusMessage(body)
        {
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            Subject = topicKey
        };
        message.ApplicationProperties["tenantId"] = tenantId.ToString();
        message.ApplicationProperties["topic"] = topicKey;
        await sender.SendMessageAsync(message, cancellationToken);
    }

    public ValueTask DisposeAsync() => _client.DisposeAsync();

    private static string NormalizeNamespace(string value) =>
        value.Trim().Replace("https://", "", StringComparison.OrdinalIgnoreCase).TrimEnd('/');
}
