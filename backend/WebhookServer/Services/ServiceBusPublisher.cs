using System.Collections.Concurrent;
using Azure.Identity;
using Azure.Messaging.ServiceBus;

namespace WebhookServer.Services;

public sealed class ServiceBusPublisher : IAsyncDisposable
{
    private readonly ConcurrentDictionary<string, ServiceBusClient> _connectionStringClients = new();
    private readonly ConcurrentDictionary<string, ServiceBusClient> _managedIdentityClients = new();

    public async Task PublishAsync(
        bool useManagedIdentity,
        string? fullyQualifiedNamespace,
        string? connectionString,
        string serviceBusEntityName,
        string body,
        Guid tenantId,
        string topicKey,
        string? contentType,
        CancellationToken cancellationToken)
    {
        var client = useManagedIdentity
            ? _managedIdentityClients.GetOrAdd(
                fullyQualifiedNamespace!,
                value => new ServiceBusClient(value, new DefaultAzureCredential()))
            : _connectionStringClients.GetOrAdd(
                connectionString!,
                value => new ServiceBusClient(value));

        await using var sender = client.CreateSender(serviceBusEntityName);
        var message = new ServiceBusMessage(body)
        {
            ContentType = string.IsNullOrWhiteSpace(contentType) ? "application/octet-stream" : contentType,
            Subject = topicKey
        };
        message.ApplicationProperties["tenantId"] = tenantId.ToString();
        message.ApplicationProperties["topic"] = topicKey;
        await sender.SendMessageAsync(message, cancellationToken);
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var client in _connectionStringClients.Values.Concat(_managedIdentityClients.Values))
        {
            await client.DisposeAsync();
        }
    }
}
