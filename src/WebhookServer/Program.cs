var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var requests = new List<ReceivedRequest>();

app.MapPost("/test", async (HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body);
    string text = await reader.ReadToEndAsync();

    requests.Add(new ReceivedRequest { TopicName = "test", Payload = text, ReceivedAt = DateTimeOffset.Now });

    return Results.Text(text, "application/json");
});

app.MapGet("/", () => requests.OrderByDescending(x => x.ReceivedAt));

app.MapGet("/reset", () =>
{
    requests.Clear();
    return Results.Redirect("/");
});

app.MapPost("/tenants/{tenantId}/topics/{topicName}", async (string tenantId, string topicName, HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body);
    string text = await reader.ReadToEndAsync();

    requests.Add(new ReceivedRequest { TenantId = tenantId, TopicName = topicName, Payload = text, ReceivedAt = DateTimeOffset.Now });

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

app.Run();

class ReceivedRequest
{
    public string? TenantId { get; set; }

    public string? TopicName { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public string? Payload { get; set; }
}
