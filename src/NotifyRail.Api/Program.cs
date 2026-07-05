using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using NotifyRail.Api.Features.Deliveries.ProviderCallbacks.Mock;
using NotifyRail.Api.Features.Deliveries.Providers;
using NotifyRail.Api.Features.Deliveries.Queue;
using NotifyRail.Api.Features.Deliveries.Worker;
using NotifyRail.Api.Features.Health;
using NotifyRail.Api.Features.Messages.CreateMessage;
using NotifyRail.Api.Features.Messages.GetMessage;
using NotifyRail.Api.Features.Messages.GetMessageDeliveries;
using NotifyRail.Api.Features.Messages.GetMessageReport;
using NotifyRail.Api.Features.Otp;
using NotifyRail.Api.Features.Otp.SendOtp;
using NotifyRail.Api.Features.Otp.VerifyOtp;
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
    builder.Services.AddOptions<DeliveryQueueOptions>()
        .Bind(builder.Configuration.GetSection(DeliveryQueueOptions.SectionName))
        .Validate(options => options.BaseRetryDelay > TimeSpan.Zero,
            "DeliveryQueue:BaseRetryDelay must be greater than zero.")
        .ValidateOnStart();
    builder.Services.Configure<MockProviderOptions>(
        builder.Configuration.GetSection(MockProviderOptions.SectionName));
    builder.Services.AddOptions<OtpOptions>()
        .Bind(builder.Configuration.GetSection(OtpOptions.SectionName))
        .Validate(options => !string.IsNullOrWhiteSpace(options.Secret),
            "Otp:Secret is required.")
        .Validate(options => !string.IsNullOrWhiteSpace(options.SenderTitle),
            "Otp:SenderTitle is required.")
        .Validate(options => options.Ttl > TimeSpan.Zero,
            "Otp:Ttl must be greater than zero.")
        .Validate(options => options.MaxAttempts > 0,
            "Otp:MaxAttempts must be greater than zero.")
        .ValidateOnStart();
    builder.Services.AddSingleton(TimeProvider.System);
    builder.Services.AddSingleton<IProviderSender, MockProvider>();
    builder.Services.AddScoped<DeliveryQueue>();
    builder.Services.AddScoped<DeliveryWorker>();
    builder.Services.AddScoped<MockProviderCallbackHandler>();
    builder.Services.AddHostedService<DeliveryWorkerBackgroundService>();
    builder.Services.AddScoped<MessageIntake>();
    builder.Services.AddScoped<MessageSummaryReader>();
    builder.Services.AddScoped<MessageDeliveryReader>();
    builder.Services.AddScoped<MessageReportReader>();
    builder.Services.AddSingleton<OtpCode>();
    builder.Services.AddScoped<OtpSender>();
    builder.Services.AddScoped<OtpVerifier>();
}

var app = builder.Build();

app.MapHealthEndpoints();
app.MapCreateMessageEndpoint();
app.MapGetMessageEndpoint();
app.MapGetMessageDeliveriesEndpoint();
app.MapGetMessageReportEndpoint();
app.MapMockProviderCallbackEndpoint();
app.MapSendOtpEndpoint();
app.MapVerifyOtpEndpoint();

app.Run();

public partial class Program;
