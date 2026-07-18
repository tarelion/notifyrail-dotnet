using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using NotifyRail.Api.Authentication;
using NotifyRail.Api.Features.ApiClients.CreateApiClient;
using NotifyRail.Api.Features.ApiClients.CreateApiKey;
using NotifyRail.Api.Features.ApiClients.DisableApiClient;
using NotifyRail.Api.Features.ApiClients.GetCurrentApiClient;
using NotifyRail.Api.Features.ApiClients.ListApiKeys;
using NotifyRail.Api.Features.ApiClients.RevokeApiKey;
using NotifyRail.Api.Features.Deliveries.ProviderCallbacks.Mock;
using NotifyRail.Api.Features.Deliveries.ProviderCallbacks;
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
using NotifyRail.Api.Features.Webhooks;
using NotifyRail.Api.Features.Webhooks.DisableWebhookEndpoint;
using NotifyRail.Api.Features.Webhooks.InspectWebhookEndpoint;
using NotifyRail.Api.Features.Webhooks.RegisterWebhookEndpoint;
using NotifyRail.Api.Features.Webhooks.Secrets;
using NotifyRail.Api.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddNotifyRailHealth(builder.Configuration);
builder.Services.AddNotifyRailAuthentication();

var dataProtection = builder.Services.AddDataProtection()
    .SetApplicationName("NotifyRail");
var dataProtectionKeyRingPath =
    builder.Configuration[$"{WebhookOptions.SectionName}:DataProtectionKeyRingPath"];
if (!string.IsNullOrWhiteSpace(dataProtectionKeyRingPath))
{
    dataProtection.PersistKeysToFileSystem(new DirectoryInfo(dataProtectionKeyRingPath));
}

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
    builder.Services.AddOptions<MockProviderCallbackOptions>()
        .Bind(builder.Configuration.GetSection(MockProviderCallbackOptions.SectionName))
        .Validate(options => !string.IsNullOrWhiteSpace(options.Secret),
            "MockProviderCallback:Secret is required.")
        .Validate(options => options.SignatureTolerance > TimeSpan.Zero,
            "MockProviderCallback:SignatureTolerance must be greater than zero.")
        .ValidateOnStart();
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
    builder.Services.Configure<WebhookOptions>(
        builder.Configuration.GetSection(WebhookOptions.SectionName));
    builder.Services.AddSingleton<IWebhookSecretProtector, DataProtectionWebhookSecretProtector>();
    builder.Services.AddSingleton<WebhookEndpointUrlValidator>();
    builder.Services.AddScoped<WebhookEndpointRegistrar>();
    builder.Services.AddScoped<WebhookEndpointReader>();
    builder.Services.AddScoped<WebhookEndpointDisabler>();
    builder.Services.AddScoped<ApiClientCreator>();
    builder.Services.AddScoped<ApiKeyCreator>();
    builder.Services.AddScoped<ApiClientDisabler>();
    builder.Services.AddScoped<CurrentApiClientReader>();
    builder.Services.AddScoped<ApiKeyMetadataReader>();
    builder.Services.AddScoped<ApiKeyRevoker>();
    builder.Services.AddSingleton<IProviderSender, MockProvider>();
    builder.Services.AddScoped<DeliveryQueue>();
    builder.Services.AddScoped<DeliveryWorker>();
    builder.Services.AddScoped<MockProviderCallbackHandler>();
    builder.Services.AddSingleton<IProviderCallbackVerifier, MockProviderCallbackVerifier>();
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

if (args.Contains("--migrate", StringComparer.OrdinalIgnoreCase))
{
    await using var scope = app.Services.CreateAsyncScope();
    var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
    await dbContext.Database.MigrateAsync();
    return;
}

app.UseAuthentication();
app.UseAuthorization();

app.MapHealthEndpoints();
app.MapCreateApiClientEndpoint();
app.MapCreateApiKeyEndpoint();
app.MapDisableApiClientEndpoint();
app.MapGetCurrentApiClientEndpoint();
app.MapListApiKeysEndpoint();
app.MapRevokeApiKeyEndpoint();
app.MapRegisterWebhookEndpoint();
app.MapInspectWebhookEndpoint();
app.MapDisableWebhookEndpoint();
app.MapCreateMessageEndpoint();
app.MapGetMessageEndpoint();
app.MapGetMessageDeliveriesEndpoint();
app.MapGetMessageReportEndpoint();
app.MapMockProviderCallbackEndpoint();
app.MapSendOtpEndpoint();
app.MapVerifyOtpEndpoint();

app.Run();

public partial class Program;
