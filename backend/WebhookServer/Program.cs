using System.Collections.Concurrent;
using System.Text.RegularExpressions;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using WebhookServer.Contracts;
using WebhookServer.Data;
using WebhookServer.Models;
using WebhookServer.Services;

var builder = WebApplication.CreateBuilder(args);
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("ConnectionStrings:DefaultConnection is required.");

builder.Services.AddDbContext<WebhookDbContext>(options => options.UseSqlServer(connectionString));
builder.Services.AddSingleton<ServiceBusPublisher>();
builder.Services.AddSingleton<ConcurrentQueue<ReceivedRequest>>();
builder.Services.AddSignalR();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins(builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? ["http://localhost:5173"])
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

await using (var scope = app.Services.CreateAsyncScope())
{
    await scope.ServiceProvider.GetRequiredService<WebhookDbContext>().Database.MigrateAsync();
}

app.UseCors();
app.MapGet("/healthz", () => Results.Ok(new { status = "Healthy" }));
app.MapGet("/", (ConcurrentQueue<ReceivedRequest> requests) => requests.Reverse().Take(500));
app.MapPost("/reset", (ConcurrentQueue<ReceivedRequest> requests) => { requests.Clear(); return Results.NoContent(); });

var tenants = app.MapGroup("/api/tenants");

tenants.MapGet("/", async (WebhookDbContext db, CancellationToken ct) =>
    await db.Tenants.AsNoTracking().OrderBy(x => x.Name)
        .Select(x => new TenantResponse(x.Id, x.Name, x.IsEnabled, x.CreatedAt, x.UpdatedAt, x.Topics.Count))
        .ToListAsync(ct));

tenants.MapPost("/", async (TenantRequest request, WebhookDbContext db, CancellationToken ct) =>
{
    var error = ValidateTenant(request);
    if (error is not null) return error;
    var tenant = new Tenant { Name = request.Name.Trim(), IsEnabled = request.IsEnabled };
    db.Tenants.Add(tenant);
    await db.SaveChangesAsync(ct);
    return Results.Created($"/api/tenants/{tenant.Id}", ToResponse(tenant, 0));
});

tenants.MapPut("/{tenantId:guid}", async (Guid tenantId, TenantRequest request, WebhookDbContext db, CancellationToken ct) =>
{
    var error = ValidateTenant(request);
    if (error is not null) return error;
    var tenant = await db.Tenants.FindAsync([tenantId], ct);
    if (tenant is null) return Results.NotFound();
    tenant.Name = request.Name.Trim();
    tenant.IsEnabled = request.IsEnabled;
    tenant.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return Results.Ok(ToResponse(tenant, await db.Topics.CountAsync(x => x.TenantId == tenantId, ct)));
});

tenants.MapPatch("/{tenantId:guid}/enabled", async (Guid tenantId, EnabledRequest request, WebhookDbContext db, CancellationToken ct) =>
{
    var tenant = await db.Tenants.FindAsync([tenantId], ct);
    if (tenant is null) return Results.NotFound();
    tenant.IsEnabled = request.IsEnabled;
    tenant.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
});

tenants.MapDelete("/{tenantId:guid}", async (Guid tenantId, WebhookDbContext db, CancellationToken ct) =>
{
    var tenant = await db.Tenants.FindAsync([tenantId], ct);
    if (tenant is null) return Results.NotFound();
    db.Tenants.Remove(tenant);
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
});

tenants.MapGet("/{tenantId:guid}/topics", async (Guid tenantId, WebhookDbContext db, CancellationToken ct) =>
{
    if (!await db.Tenants.AnyAsync(x => x.Id == tenantId, ct)) return Results.NotFound();
    var topics = await db.Topics.AsNoTracking().Where(x => x.TenantId == tenantId).OrderBy(x => x.Name).ToListAsync(ct);
    return Results.Ok(topics.Select(ToTopicResponse));
});

tenants.MapPost("/{tenantId:guid}/topics", async (Guid tenantId, TopicRequest request, WebhookDbContext db, CancellationToken ct) =>
{
    var error = ValidateTopic(request, isCreate: true, hasStoredConnection: false);
    if (error is not null) return error;
    if (!await db.Tenants.AnyAsync(x => x.Id == tenantId, ct)) return Results.NotFound();
    var key = NormalizeKey(request.Key);
    if (await db.Topics.AnyAsync(x => x.TenantId == tenantId && x.Key == key, ct))
        return Results.Conflict(new { error = "A topic with this key already exists in the tenant." });

    var topic = new Topic
    {
        TenantId = tenantId, Key = key, Name = request.Name.Trim(), IsEnabled = request.IsEnabled,
        IsSharePointWebhook = request.IsSharePointWebhook,
        UseManagedIdentity = request.UseManagedIdentity,
        FullyQualifiedNamespace = request.UseManagedIdentity ? NormalizeNamespace(request.FullyQualifiedNamespace!) : null,
        ServiceBusConnectionString = request.UseManagedIdentity ? null : request.ServiceBusConnectionString!.Trim(),
        ServiceBusTopicName = request.ServiceBusTopicName.Trim()
    };
    db.Topics.Add(topic);
    await db.SaveChangesAsync(ct);
    return Results.Created($"/api/tenants/{tenantId}/topics/{topic.Id}", ToTopicResponse(topic));
});

tenants.MapPut("/{tenantId:guid}/topics/{topicId:guid}", async (Guid tenantId, Guid topicId, TopicRequest request, WebhookDbContext db, CancellationToken ct) =>
{
    var topic = await db.Topics.SingleOrDefaultAsync(x => x.Id == topicId && x.TenantId == tenantId, ct);
    if (topic is null) return Results.NotFound();
    var error = ValidateTopic(request, isCreate: false, hasStoredConnection: !string.IsNullOrWhiteSpace(topic.ServiceBusConnectionString));
    if (error is not null) return error;
    var key = NormalizeKey(request.Key);
    if (await db.Topics.AnyAsync(x => x.TenantId == tenantId && x.Id != topicId && x.Key == key, ct))
        return Results.Conflict(new { error = "A topic with this key already exists in the tenant." });

    topic.Key = key;
    topic.Name = request.Name.Trim();
    topic.IsEnabled = request.IsEnabled;
    topic.IsSharePointWebhook = request.IsSharePointWebhook;
    topic.UseManagedIdentity = request.UseManagedIdentity;
    topic.FullyQualifiedNamespace = request.UseManagedIdentity ? NormalizeNamespace(request.FullyQualifiedNamespace!) : null;
    topic.ServiceBusTopicName = request.ServiceBusTopicName.Trim();
    if (request.UseManagedIdentity) topic.ServiceBusConnectionString = null;
    else if (!string.IsNullOrWhiteSpace(request.ServiceBusConnectionString)) topic.ServiceBusConnectionString = request.ServiceBusConnectionString.Trim();
    topic.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return Results.Ok(ToTopicResponse(topic));
});

tenants.MapPatch("/{tenantId:guid}/topics/{topicId:guid}/enabled", async (Guid tenantId, Guid topicId, EnabledRequest request, WebhookDbContext db, CancellationToken ct) =>
{
    var topic = await db.Topics.SingleOrDefaultAsync(x => x.Id == topicId && x.TenantId == tenantId, ct);
    if (topic is null) return Results.NotFound();
    topic.IsEnabled = request.IsEnabled;
    topic.UpdatedAt = DateTimeOffset.UtcNow;
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
});

tenants.MapDelete("/{tenantId:guid}/topics/{topicId:guid}", async (Guid tenantId, Guid topicId, WebhookDbContext db, CancellationToken ct) =>
{
    var topic = await db.Topics.SingleOrDefaultAsync(x => x.Id == topicId && x.TenantId == tenantId, ct);
    if (topic is null) return Results.NotFound();
    db.Topics.Remove(topic);
    await db.SaveChangesAsync(ct);
    return Results.NoContent();
});

app.MapPost("/tenants/{tenantId:guid}/topics/{topicKey}", async (
    Guid tenantId, string topicKey, HttpRequest request, WebhookDbContext db, ServiceBusPublisher publisher,
    ConcurrentQueue<ReceivedRequest> requests, IHubContext<WebhookHub> hub, CancellationToken ct) =>
{
    var normalizedTopic = NormalizeKey(topicKey);
    var topic = await db.Topics.AsNoTracking().Include(x => x.Tenant)
        .SingleOrDefaultAsync(x => x.TenantId == tenantId && x.Key == normalizedTopic, ct);
    if (topic is null) return Results.NotFound(new { error = "Webhook endpoint was not found." });
    if (!topic.Tenant.IsEnabled || !topic.IsEnabled)
        return Results.Json(new { error = "This webhook endpoint is disabled." }, statusCode: StatusCodes.Status403Forbidden);
    // SharePoint validation request: echo the token without publishing a message.
    if (topic.IsSharePointWebhook && request.Query.TryGetValue("validationtoken", out var token))
    {
        return Results.Text(token.ToString(), "text/plain");
    }

    using var reader = new StreamReader(request.Body);
    var body = await reader.ReadToEndAsync(ct);
    try
    {
        await publisher.PublishAsync(topic.UseManagedIdentity, topic.FullyQualifiedNamespace, topic.ServiceBusConnectionString,
            topic.ServiceBusTopicName, body, topic.TenantId, topic.Key, request.ContentType, ct);
    }
    catch (Exception exception)
    {
        app.Logger.LogError(exception, "Failed to publish webhook for tenant {TenantId} and topic {TopicKey}", tenantId, topicKey);
        return Results.Problem("Azure Service Bus rejected or could not receive the message.", statusCode: StatusCodes.Status502BadGateway);
    }

    var received = new ReceivedRequest(Guid.NewGuid(), topic.TenantId.ToString(), topic.Key, DateTimeOffset.UtcNow, body);
    requests.Enqueue(received);
    while (requests.Count > 500) requests.TryDequeue(out _);
    await hub.Clients.All.SendAsync("WebhookReceived", received, ct);
    return Results.Accepted(value: received);
});

app.MapHub<WebhookHub>("/hubs/events");
app.Run();

static IResult? ValidateTenant(TenantRequest request)
{
    if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200) return Results.BadRequest(new { error = "Tenant name is required and must be at most 200 characters." });
    return null;
}

static IResult? ValidateTopic(TopicRequest request, bool isCreate, bool hasStoredConnection)
{
    if (!ValidKey(request.Key)) return Results.BadRequest(new { error = "Topic key must be 1-100 letters, numbers, or hyphens." });
    if (string.IsNullOrWhiteSpace(request.Name) || request.Name.Trim().Length > 200) return Results.BadRequest(new { error = "Topic name is required and must be at most 200 characters." });
    if (string.IsNullOrWhiteSpace(request.ServiceBusTopicName) || request.ServiceBusTopicName.Trim().Length > 260) return Results.BadRequest(new { error = "Azure Service Bus topic name is required and must be at most 260 characters." });
    if (request.UseManagedIdentity && string.IsNullOrWhiteSpace(request.FullyQualifiedNamespace)) return Results.BadRequest(new { error = "Fully qualified namespace is required for managed identity." });
    if (request.UseManagedIdentity && request.FullyQualifiedNamespace!.Trim().Length > 300) return Results.BadRequest(new { error = "Fully qualified namespace must be at most 300 characters." });
    if (!request.UseManagedIdentity && string.IsNullOrWhiteSpace(request.ServiceBusConnectionString) && (isCreate || !hasStoredConnection)) return Results.BadRequest(new { error = "Connection string is required when managed identity is disabled." });
    if (!string.IsNullOrWhiteSpace(request.ServiceBusConnectionString) && request.ServiceBusConnectionString.Trim().Length > 2000) return Results.BadRequest(new { error = "Connection string must be at most 2000 characters." });
    return null;
}

static bool ValidKey(string? key) => !string.IsNullOrWhiteSpace(key) && key.Trim().Length <= 100 && Regex.IsMatch(key.Trim(), "^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.IgnoreCase);
static string NormalizeKey(string value) => value.Trim().ToLowerInvariant();
static string NormalizeNamespace(string value) => value.Trim().Replace("https://", "", StringComparison.OrdinalIgnoreCase).TrimEnd('/');
static TenantResponse ToResponse(Tenant tenant, int count) => new(tenant.Id, tenant.Name, tenant.IsEnabled, tenant.CreatedAt, tenant.UpdatedAt, count);
static TopicResponse ToTopicResponse(Topic topic) => new(topic.Id, topic.TenantId, topic.Key, topic.Name, topic.IsEnabled,
    topic.IsSharePointWebhook, topic.UseManagedIdentity, topic.FullyQualifiedNamespace, !string.IsNullOrWhiteSpace(topic.ServiceBusConnectionString),
    topic.ServiceBusTopicName, topic.CreatedAt, topic.UpdatedAt);

public sealed record ReceivedRequest(Guid Id, string TenantId, string TopicName, DateTimeOffset ReceivedAt, string Payload);
public sealed class WebhookHub : Hub;
public partial class Program;
