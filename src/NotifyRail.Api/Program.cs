using Microsoft.AspNetCore.Builder;
using NotifyRail.Api.Features.Health;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNotifyRailHealth(builder.Configuration);

var app = builder.Build();

app.MapHealthEndpoints();

app.Run();

public partial class Program;
