using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using NotifyRail.Api.Features.Health;
using NotifyRail.Api.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNotifyRailHealth(builder.Configuration);

var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres");
if (!string.IsNullOrWhiteSpace(postgresConnectionString))
{
    builder.Services.AddDbContext<NotifyRailDbContext>(options =>
        options.UseNpgsql(postgresConnectionString));
}

var app = builder.Build();

app.MapHealthEndpoints();

app.Run();

public partial class Program;
