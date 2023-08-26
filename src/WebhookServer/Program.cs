var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

var requests = new List<RecivedRequest>();

app.MapPost("/test", async (HttpRequest request) =>
{
    using var reader = new StreamReader(request.Body);
    string text = await reader.ReadToEndAsync();

    requests.Add(new RecivedRequest { Payload = text, RecievedDate = DateTimeOffset.Now });

    return Results.Text(text, "application/json");
});

app.MapGet("/", () => requests.OrderByDescending(x => x.RecievedDate));

app.MapGet("/reset", () =>
{
    requests.Clear();
    return Results.Redirect("/");
});

app.Run();

class RecivedRequest
{
    public DateTimeOffset RecievedDate { get; set; }

    public string? Payload { get; set; }
}
