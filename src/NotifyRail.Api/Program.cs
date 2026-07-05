using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using NotifyRail.Api.Features.Deliveries.Providers;
using NotifyRail.Api.Features.Deliveries.Queue;
using NotifyRail.Api.Features.Deliveries.Worker;
using NotifyRail.Api.Features.Health;
using NotifyRail.Api.Features.Messages.CreateMessage;
using NotifyRail.Api.Features.Messages.GetMessageDeliveries;
using NotifyRail.Api.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNotifyRailHealth(builder.Configuration);

var postgresConnectionString = builder.Configuration.GetConnectionString("Postgres");
if (!string.IsNullOrWhiteSpace(postgresConnectionString))
{
    builder.Services.AddDbContext<NotifyRailDbContext>(options =>
        options.UseNpgsql(postgresConnectionString));
    builder.Services.Configure<DeliveryWorkerOptions>(
        builder.Configuration.GetSection(DeliveryWorkerOptions.SectionName));
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<IProviderSender, MockProvider>();
    builder.Services.AddScoped<DeliveryQueue>();
    builder.Services.AddScoped<DeliveryWorker>();
    builder.Services.AddHostedService<DeliveryWorkerBackgroundService>();
    builder.Services.AddScoped<MessageIntake>();
    builder.Services.AddScoped<MessageDeliveryReader>();
}

var app = builder.Build();

app.MapHealthEndpoints();
app.MapCreateMessageEndpoint();
app.MapGetMessageDeliveriesEndpoint();

app.Run();

public partial class Program;
