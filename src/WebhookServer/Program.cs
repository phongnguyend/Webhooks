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

app.MapPost("/topics/{topicName}", async (string topicName, HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body);
    string text = await reader.ReadToEndAsync();

    requests.Add(new ReceivedRequest { TopicName = topicName, Payload = text, ReceivedAt = DateTimeOffset.Now });

    return Results.Text(text, "application/json");
});

app.MapGet("/topics/{topicName}", (string topicName) => requests
    .Where(x => x.TopicName == topicName)
    .OrderByDescending(x => x.ReceivedAt));

app.MapDelete("/topics/{topicName}", (string topicName) =>
{
    requests.RemoveAll(x => x.TopicName == topicName);
    return Results.Ok();
});

app.Run();

class ReceivedRequest
{
    public string? TopicName { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public string? Payload { get; set; }
}
