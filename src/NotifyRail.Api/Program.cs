var builder = WebApplication.CreateBuilder(args);
var app = builder.Build();

app.MapGet("/healthz", () => Results.Ok(new HealthResponse("ok")));

app.Run();

public sealed record HealthResponse(string Status);

public partial class Program;
