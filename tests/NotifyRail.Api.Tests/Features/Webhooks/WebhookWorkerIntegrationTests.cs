using System.Net;
using System.Collections.Concurrent;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NotifyRail.Api.Features.ApiClients.CreateApiClient;
using NotifyRail.Api.Features.Deliveries.Queue;
using NotifyRail.Api.Features.Messages.CreateMessage;
using NotifyRail.Api.Features.Webhooks.RegisterWebhookEndpoint;
using NotifyRail.Api.Features.Webhooks.Dispatch;
using NotifyRail.Api.Features.Webhooks.Queue;
using NotifyRail.Api.Features.Webhooks.Worker;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Tests;

public sealed class WebhookWorkerIntegrationTests
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public WebhookWorkerIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory
            .WithMessageApiAuthentication()
            .WithoutHostedServices();
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    [Fact]
    public async Task ProcessBatchAsync_SendsSignedExactPayloadAndRecordsSuccessfulAttempt()
    {
        await ResetDatabaseAsync();
        await using var receiver = await TestWebhookReceiver.StartAsync(HttpStatusCode.NoContent);
        var (apiClientId, secret) = await CreateApiClientWithEndpointAsync(receiver.Url);
        await CreateMessageAsync(apiClientId);
        await RecordAcceptedDeliveryAsync();
        var dispatchAt = TruncateToMicroseconds(DateTimeOffset.UtcNow);

        await using var scope = _factory.Services.CreateAsyncScope();
        var worker = scope.ServiceProvider.GetRequiredService<WebhookWorker>();
        var processed = await worker.ProcessBatchAsync(dispatchAt, CancellationToken.None);

        var received = await receiver.Received.WaitAsync(TimeSpan.FromSeconds(3));
        var state = await LoadWebhookStateAsync();
        Assert.Equal(1, processed);
        Assert.Equal(state.EventId.ToString(), received.EventId);
        Assert.Equal(state.AttemptedAt.ToUnixTimeSeconds().ToString(), received.Timestamp);
        Assert.Equal(state.Payload, received.Body);
        Assert.Equal(
            Sign(secret, received.Timestamp, received.Body),
            received.Signature);
        Assert.Equal("succeeded", state.EventStatus);
        Assert.Equal(1, state.EventAttemptCount);
        Assert.True(state.AttemptedAt >= dispatchAt);
        Assert.NotNull(state.EventSucceededAt);
        Assert.Equal(state.CompletedAt, state.EventSucceededAt);
        Assert.True(state.CompletedAt > state.AttemptedAt);
        Assert.Equal(1, state.AttemptNumber);
        Assert.Equal("succeeded", state.AttemptOutcome);
        Assert.Equal(204, state.HttpStatusCode);
        Assert.Null(state.ErrorCode);
        Assert.Null(state.ErrorMessage);
    }

    [Fact]
    public async Task ProcessBatchAsync_RecordsBoundedFailureWithoutChangingDeliveryTruth()
    {
        await ResetDatabaseAsync();
        var remoteBody = new string('x', 2_000);
        await using var receiver = await TestWebhookReceiver.StartAsync(
            HttpStatusCode.InternalServerError,
            remoteBody);
        var (apiClientId, _) = await CreateApiClientWithEndpointAsync(receiver.Url);
        await CreateMessageAsync(apiClientId);
        await RecordAcceptedDeliveryAsync();
        var dispatchAt = TruncateToMicroseconds(DateTimeOffset.UtcNow);

        await using var scope = _factory.Services.CreateAsyncScope();
        var worker = scope.ServiceProvider.GetRequiredService<WebhookWorker>();
        var processed = await worker.ProcessBatchAsync(dispatchAt, CancellationToken.None);

        var state = await LoadWebhookStateAsync();
        Assert.Equal(1, processed);
        Assert.Equal("sent", state.DeliveryStatus);
        Assert.Equal("failed", state.EventStatus);
        Assert.Equal("failed", state.AttemptOutcome);
        Assert.Equal(500, state.HttpStatusCode);
        Assert.Equal("http_error", state.ErrorCode);
        Assert.NotNull(state.ErrorMessage);
        Assert.True(state.ErrorMessage.Length <= 500);
        Assert.DoesNotContain(remoteBody, state.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessBatchAsync_ClaimsAndSignsEachBatchItemAtDispatchTime()
    {
        await ResetDatabaseAsync();
        await using var receiver = await TestWebhookReceiver.StartAsync(HttpStatusCode.NoContent);
        var (apiClientId, _) = await CreateApiClientWithEndpointAsync(receiver.Url);
        await CreateMessageAsync(apiClientId, ["+905551111111", "+905552222222"]);
        await RecordAcceptedDeliveriesAsync(count: 2);
        var startedAt = TruncateToMicroseconds(DateTimeOffset.UtcNow);
        var timeProvider = new AdvancingTimeProvider(startedAt, TimeSpan.FromMinutes(2));

        await using var scope = _factory.Services.CreateAsyncScope();
        var worker = new WebhookWorker(
            scope.ServiceProvider.GetRequiredService<WebhookQueue>(),
            scope.ServiceProvider.GetRequiredService<WebhookDispatcher>(),
            Options.Create(new WebhookWorkerOptions
            {
                WorkerId = "batch-webhook-worker",
                BatchSize = 2,
            }),
            timeProvider,
            scope.ServiceProvider.GetRequiredService<ILogger<WebhookWorker>>());

        var processed = await worker.ProcessBatchAsync(startedAt, CancellationToken.None);

        var received = await receiver.WaitForCountAsync(2);
        Assert.Equal(2, processed);
        Assert.Equal(2, received.Count);
        Assert.NotEqual(received[0].Timestamp, received[1].Timestamp);
        Assert.True(long.Parse(received[1].Timestamp) > long.Parse(received[0].Timestamp));
    }

    private async Task<(Guid ApiClientId, string Secret)> CreateApiClientWithEndpointAsync(
        string endpointUrl)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var creator = scope.ServiceProvider.GetRequiredService<ApiClientCreator>();
        var created = await creator.CreateAsync("Webhook worker client", CancellationToken.None);
        var registrar = scope.ServiceProvider.GetRequiredService<WebhookEndpointRegistrar>();
        var registered = await registrar.RegisterAsync(
            created.ApiClientId,
            endpointUrl,
            CancellationToken.None);
        Assert.NotNull(registered);
        Assert.NotNull(registered.WebhookSecret);
        return (created.ApiClientId, registered.WebhookSecret);
    }

    private async Task CreateMessageAsync(
        Guid apiClientId,
        IReadOnlyList<string>? recipients = null)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var intake = scope.ServiceProvider.GetRequiredService<MessageIntake>();
        var outcome = await intake.CreateAsync(
            apiClientId,
            new CreateMessageCommand(
                Type: "transactional",
                Channel: "sms",
                SenderTitle: "NotifyRail",
                Body: "Your order is ready.",
                Recipients: recipients ?? ["+905551111111"],
                IdempotencyKey: $"webhook-worker-{Guid.NewGuid()}",
                ScheduledAt: null,
                ReportLabel: null,
                Encoding: null),
            CancellationToken.None);
        Assert.NotNull(outcome.Response);
    }

    private async Task RecordAcceptedDeliveryAsync()
    {
        await RecordAcceptedDeliveriesAsync(count: 1);
    }

    private async Task RecordAcceptedDeliveriesAsync(int count)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<DeliveryQueue>();
        for (var index = 0; index < count; index++)
        {
            var job = Assert.Single(await queue.ClaimDueAsync(
                "delivery-worker",
                limit: 1,
                DateTimeOffset.UtcNow,
                CancellationToken.None));
            await queue.RecordProviderResultAsync(
                job.Claim,
                new ProviderResult(
                    ProviderOutcome.Accepted,
                    Provider: "mock",
                    ProviderMessageId: $"provider-message-{index + 1}"),
                DateTimeOffset.UtcNow,
                CancellationToken.None);
        }
    }

    private async Task ResetDatabaseAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
        await dbContext.Database.MigrateAsync(CancellationToken.None);
        await dbContext.Database.ExecuteSqlRawAsync(
            "TRUNCATE webhook_attempts, webhook_events, webhook_secrets, webhook_endpoints, " +
            "otp_challenges, delivery_attempts, deliveries, messages, api_keys, api_clients CASCADE;",
            CancellationToken.None);
    }

    private async Task<WebhookState> LoadWebhookStateAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
        return await dbContext.Database.SqlQueryRaw<WebhookState>(
            """
            SELECT
                deliveries.status AS "DeliveryStatus",
                webhook_events.id AS "EventId",
                webhook_events.payload AS "Payload",
                webhook_events.status AS "EventStatus",
                webhook_events.attempt_count AS "EventAttemptCount",
                webhook_events.succeeded_at AS "EventSucceededAt",
                webhook_attempts.attempt_number AS "AttemptNumber",
                webhook_attempts.outcome AS "AttemptOutcome",
                webhook_attempts.http_status_code AS "HttpStatusCode",
                webhook_attempts.error_code AS "ErrorCode",
                webhook_attempts.error_message AS "ErrorMessage",
                webhook_attempts.attempted_at AS "AttemptedAt",
                webhook_attempts.completed_at AS "CompletedAt"
            FROM webhook_events
            JOIN deliveries ON deliveries.id = webhook_events.delivery_id
            JOIN webhook_attempts ON webhook_attempts.webhook_event_id = webhook_events.id
            """).SingleAsync(CancellationToken.None);
    }

    private static string Sign(string secret, string timestamp, string body)
    {
        var content = Encoding.UTF8.GetBytes($"{timestamp}.{body}");
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), content);
        return $"v1={Convert.ToHexStringLower(hash)}";
    }

    private sealed class WebhookState
    {
        public string DeliveryStatus { get; init; } = null!;
        public Guid EventId { get; init; }
        public string Payload { get; init; } = null!;
        public string EventStatus { get; init; } = null!;
        public int EventAttemptCount { get; init; }
        public DateTimeOffset? EventSucceededAt { get; init; }
        public int AttemptNumber { get; init; }
        public string AttemptOutcome { get; init; } = null!;
        public int? HttpStatusCode { get; init; }
        public string? ErrorCode { get; init; }
        public string? ErrorMessage { get; init; }
        public DateTimeOffset AttemptedAt { get; init; }
        public DateTimeOffset CompletedAt { get; init; }
    }

    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value)
    {
        return value.AddTicks(-(value.Ticks % 10));
    }

    private sealed class TestWebhookReceiver : IAsyncDisposable
    {
        private readonly WebApplication _application;
        private readonly ConcurrentQueue<ReceivedWebhook> _receivedWebhooks;

        private TestWebhookReceiver(
            WebApplication application,
            string url,
            Task<ReceivedWebhook> received,
            ConcurrentQueue<ReceivedWebhook> receivedWebhooks)
        {
            _application = application;
            Url = url;
            Received = received;
            _receivedWebhooks = receivedWebhooks;
        }

        public string Url { get; }
        public Task<ReceivedWebhook> Received { get; }

        public async Task<IReadOnlyList<ReceivedWebhook>> WaitForCountAsync(int count)
        {
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            while (_receivedWebhooks.Count < count)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(10), timeout.Token);
            }

            return _receivedWebhooks.ToArray();
        }

        public static async Task<TestWebhookReceiver> StartAsync(
            HttpStatusCode statusCode,
            string? responseBody = null)
        {
            var received = new TaskCompletionSource<ReceivedWebhook>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var receivedWebhooks = new ConcurrentQueue<ReceivedWebhook>();
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var application = builder.Build();
            application.MapPost("/webhooks", async context =>
            {
                using var reader = new StreamReader(context.Request.Body, Encoding.UTF8);
                var body = await reader.ReadToEndAsync(context.RequestAborted);
                var webhook = new ReceivedWebhook(
                    context.Request.Headers["X-NotifyRail-Event-Id"].ToString(),
                    context.Request.Headers["X-NotifyRail-Timestamp"].ToString(),
                    context.Request.Headers["X-NotifyRail-Signature"].ToString(),
                    body);
                receivedWebhooks.Enqueue(webhook);
                received.TrySetResult(webhook);
                context.Response.StatusCode = (int)statusCode;
                if (responseBody is not null)
                {
                    await context.Response.WriteAsync(responseBody, context.RequestAborted);
                }
            });
            await application.StartAsync();
            var address = application.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new TestWebhookReceiver(
                application,
                $"{address}/webhooks",
                received.Task,
                receivedWebhooks);
        }

        public async ValueTask DisposeAsync()
        {
            await _application.StopAsync();
            await _application.DisposeAsync();
        }
    }

    private sealed record ReceivedWebhook(
        string EventId,
        string Timestamp,
        string Signature,
        string Body);

    private sealed class AdvancingTimeProvider(
        DateTimeOffset start,
        TimeSpan step) : TimeProvider
    {
        private long _callCount = -1;

        public override DateTimeOffset GetUtcNow()
        {
            return start.AddTicks(step.Ticks * Interlocked.Increment(ref _callCount));
        }
    }
}
