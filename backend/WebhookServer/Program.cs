using Microsoft.AspNetCore.SignalR;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddSignalR();
builder.Services.AddCors(options => options.AddDefaultPolicy(policy => policy
    .WithOrigins("http://localhost:5173")
    .AllowAnyHeader()
    .AllowAnyMethod()
    .AllowCredentials()));

var app = builder.Build();

app.UseCors();

var requests = new List<ReceivedRequest>();

app.MapGet("/healthz", () => Results.Ok(new { status = "Healthy" }));

app.MapGet("/", () => requests.OrderByDescending(x => x.ReceivedAt));

app.MapGet("/reset", () =>
{
    requests.Clear();
    return Results.Redirect("/");
});

app.MapPost("/tenants/{tenantId}/topics/{topicName}", async (
    string tenantId,
    string topicName,
    HttpRequest request,
    IHubContext<WebhookHub> hub) =>
{
    // Validation request
    if (topicName == "sharepoint" && request.Query.ContainsKey("validationtoken"))
    {
        return Results.Text(request.Query["validationtoken"], "text/plain");
    }

    using var reader = new StreamReader(request.Body);
    string text = await reader.ReadToEndAsync();

    var receivedRequest = new ReceivedRequest
    {
        Id = Guid.NewGuid(),
        TenantId = tenantId,
        TopicName = topicName,
        Payload = text,
        ReceivedAt = DateTimeOffset.Now
    };

    requests.Add(receivedRequest);
    await hub.Clients.All.SendAsync("WebhookReceived", receivedRequest);

    return Results.Text(text, "application/json");
});

app.MapGet("/tenants/{tenantId}/topics/{topicName}", (string tenantId, string topicName) => requests
    .Where(x => x.TenantId == tenantId && x.TopicName == topicName)
    .OrderByDescending(x => x.ReceivedAt));

app.MapDelete("/tenants/{tenantId}/topics/{topicName}", (string tenantId, string topicName) =>
{
    requests.RemoveAll(x => x.TenantId == tenantId && x.TopicName == topicName);
    return Results.Ok();
});

app.MapHub<WebhookHub>("/hubs/events");

app.Run();

class ReceivedRequest
{
    public Guid Id { get; set; }

    public string? TenantId { get; set; }

    public string? TopicName { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public string? Payload { get; set; }
}

class WebhookHub : Hub;
