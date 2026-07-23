using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using NotifyRail.Api.Features.ApiClients.CreateApiClient;
using NotifyRail.Api.Features.Deliveries.ProviderCallbacks.Mock;
using NotifyRail.Api.Features.Deliveries.Queue;
using NotifyRail.Api.Features.Messages.CreateMessage;
using NotifyRail.Api.Features.Webhooks.Dispatch;
using NotifyRail.Api.Features.Webhooks.Queue;
using NotifyRail.Api.Features.Webhooks.RegisterWebhookEndpoint;
using NotifyRail.Api.Features.Webhooks.Secrets;
using NotifyRail.Api.Features.Webhooks.Worker;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Tests;

public sealed class DeadWebhookEventManagementIntegrationTests : IDisposable
{
    private const string OperatorCredential = "dead-webhook-test-operator-credential";
    private readonly WebApplicationFactory<Program> _factory;

    public DeadWebhookEventManagementIntegrationTests()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithoutHostedServices()
            .WithWebHostBuilder(builder =>
                builder.UseSetting(
                    "Authentication:Operator:Credential",
                    OperatorCredential));
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    [Fact]
    public async Task InspectDeadWebhookEvent_ReturnsEventAndAttemptsWithoutSecretMaterial()
    {
        await ResetDatabaseAsync();
        var deadEvent = await CreateDeadWebhookEventAsync();
        using var client = CreateOperatorClient();

        using var response = await client.GetAsync(
            $"/management/webhook-events/{deadEvent.EventId}");
        var body = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(body);
        var root = document.RootElement;

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(deadEvent.EventId, root.GetProperty("webhook_event_id").GetGuid());
        Assert.Equal(deadEvent.ApiClientId, root.GetProperty("api_client_id").GetGuid());
        Assert.Equal(deadEvent.DeliveryId, root.GetProperty("delivery_id").GetGuid());
        Assert.Equal("delivery.sent", root.GetProperty("type").GetString());
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal(1, root.GetProperty("sequence").GetInt32());
        Assert.Equal("dead", root.GetProperty("status").GetString());
        Assert.NotEqual(default, root.GetProperty("dead_at").GetDateTimeOffset());
        Assert.Equal(1, root.GetProperty("attempt_count").GetInt32());
        var attempt = Assert.Single(root.GetProperty("attempts").EnumerateArray());
        Assert.Equal(1, attempt.GetProperty("attempt_number").GetInt32());
        Assert.Equal("permanent_failure", attempt.GetProperty("outcome").GetString());
        Assert.Equal(400, attempt.GetProperty("http_status_code").GetInt32());
        Assert.DoesNotContain(deadEvent.WebhookSecret, body, StringComparison.Ordinal);
        Assert.DoesNotContain("sensitive-endpoint-token", body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ListDeadWebhookEvents_ReturnsOnlyDeadEvents()
    {
        await ResetDatabaseAsync();
        var deadEvent = await CreateDeadWebhookEventAsync();
        await CreatePendingWebhookEventAsync(deadEvent.ApiClientId);
        using var client = CreateOperatorClient();

        using var response = await client.GetAsync("/management/webhook-events/dead");
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var item = Assert.Single(
            document.RootElement.GetProperty("webhook_events").EnumerateArray());
        Assert.Equal(deadEvent.EventId, item.GetProperty("webhook_event_id").GetGuid());
        Assert.Equal("dead", item.GetProperty("status").GetString());
        Assert.Equal(1, item.GetProperty("attempt_count").GetInt32());
    }

    [Fact]
    public async Task ReplayDeadWebhookEvent_PreservesEventAndDeliveryTruthAndCreatesNewAttempt()
    {
        await ResetDatabaseAsync();
        var deadEvent = await CreateDeadWebhookEventAsync();
        var before = await LoadReplayStateAsync();
        using var client = CreateOperatorClient();

        using var response = await client.PostAsync(
            $"/management/webhook-events/{deadEvent.EventId}/replay",
            content: null);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var replayAt = TruncateToMicroseconds(DateTimeOffset.UtcNow.AddMinutes(1));
        var handler = new RecordingWebhookHandler(HttpStatusCode.NoContent);
        using var httpClient = new HttpClient(handler);
        var options = Options.Create(new WebhookWorkerOptions
        {
            WorkerId = "manual-replay-worker",
        });
        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var worker = new WebhookWorker(
                new WebhookQueue(
                    scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>(),
                    options,
                    scope.ServiceProvider.GetRequiredService<IWebhookRetryJitter>()),
                new WebhookDispatcher(
                    httpClient,
                    scope.ServiceProvider.GetRequiredService<IWebhookSecretProtector>()),
                options,
                new FixedTimeProvider(replayAt));
            Assert.Equal(1, await worker.ProcessBatchAsync(
                replayAt,
                CancellationToken.None));
        }

        var after = await LoadReplayStateAsync();
        Assert.Equal(before.EventId, after.EventId);
        Assert.Equal(before.Version, after.Version);
        Assert.Equal(before.Sequence, after.Sequence);
        Assert.Equal(before.OccurredAt, after.OccurredAt);
        Assert.Equal(before.Payload, after.Payload);
        Assert.Equal(
            before.AutomaticAttemptDeadlineAt,
            after.AutomaticAttemptDeadlineAt);
        Assert.Equal(before.DeliveryStatus, after.DeliveryStatus);
        Assert.Equal(before.DeliveryUpdatedAt, after.DeliveryUpdatedAt);
        Assert.Equal(1, after.EventCount);
        Assert.Equal(2, after.AttemptCount);
        Assert.Equal("succeeded", after.EventStatus);
        Assert.Equal(deadEvent.EventId.ToString(), handler.EventId);
        Assert.Equal(before.Payload, handler.Body);

        using var inspection = await client.GetAsync(
            $"/management/webhook-events/{deadEvent.EventId}");
        using var deadEvents = await client.GetAsync(
            "/management/webhook-events/dead");
        using var inspectionDocument = JsonDocument.Parse(
            await inspection.Content.ReadAsStringAsync());
        using var deadEventsDocument = JsonDocument.Parse(
            await deadEvents.Content.ReadAsStringAsync());
        Assert.Equal(HttpStatusCode.OK, inspection.StatusCode);
        Assert.Equal(
            "succeeded",
            inspectionDocument.RootElement.GetProperty("status").GetString());
        Assert.NotEqual(
            default,
            inspectionDocument.RootElement.GetProperty("dead_at").GetDateTimeOffset());
        Assert.Contains(
            deadEventsDocument.RootElement
                .GetProperty("webhook_events")
                .EnumerateArray(),
            item => item.GetProperty("webhook_event_id").GetGuid() == deadEvent.EventId);
    }

    [Fact]
    public async Task ReplayDeadWebhookEvent_AllowsSequenceAwareReceiverToRejectStaleState()
    {
        await ResetDatabaseAsync();
        var deadEvent = await CreateDeadWebhookEventAsync();
        await using (var callbackScope = _factory.Services.CreateAsyncScope())
        {
            var handler = callbackScope.ServiceProvider
                .GetRequiredService<MockProviderCallbackHandler>();
            Assert.NotNull(await handler.ApplyAsync(
                "dead-webhook-provider-id",
                "delivered",
                CancellationToken.None));
        }

        var receiver = new SequenceAwareWebhookHandler();
        var dispatchAt = TruncateToMicroseconds(DateTimeOffset.UtcNow.AddMinutes(1));
        Assert.Equal(1, await ProcessWebhookBatchAsync(
            receiver,
            "newer-event-worker",
            dispatchAt));
        Assert.Equal(2, receiver.HighestSequence);

        using var client = CreateOperatorClient();
        using var response = await client.PostAsync(
            $"/management/webhook-events/{deadEvent.EventId}/replay",
            content: null);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        Assert.Equal(1, await ProcessWebhookBatchAsync(
            receiver,
            "stale-replay-worker",
            dispatchAt.AddMinutes(1)));

        var state = await LoadEventDispatchStateAsync(deadEvent.EventId);
        Assert.Equal([1], receiver.RejectedSequences);
        Assert.Equal("dead", state.Status);
        Assert.Equal(2, state.AttemptCount);
        Assert.Equal("permanent_failure", state.LastAttemptOutcome);
        Assert.Equal(409, state.LastHttpStatusCode);
        Assert.Equal("delivered", state.DeliveryStatus);
    }

    [Fact]
    public async Task ManageDeadWebhookEvents_RequiresOperatorAuthority()
    {
        await ResetDatabaseAsync();
        var deadEvent = await CreateDeadWebhookEventAsync();
        using var anonymousClient = _factory.CreateClient();
        using var apiClient = _factory.CreateClient();
        apiClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", deadEvent.ApiKey);
        var detailRoute = $"/management/webhook-events/{deadEvent.EventId}";
        var replayRoute = $"{detailRoute}/replay";

        using var anonymousList = await anonymousClient.GetAsync(
            "/management/webhook-events/dead");
        using var anonymousDetail = await anonymousClient.GetAsync(detailRoute);
        using var anonymousReplay = await anonymousClient.PostAsync(
            replayRoute,
            content: null);
        using var apiClientList = await apiClient.GetAsync(
            "/management/webhook-events/dead");
        using var apiClientDetail = await apiClient.GetAsync(detailRoute);
        using var apiClientReplay = await apiClient.PostAsync(
            replayRoute,
            content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, anonymousList.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousDetail.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousReplay.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, apiClientList.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, apiClientDetail.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, apiClientReplay.StatusCode);
    }

    private HttpClient CreateOperatorClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Operator", OperatorCredential);
        return client;
    }

    private async Task<DeadEventFixture> CreateDeadWebhookEventAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var created = await scope.ServiceProvider
            .GetRequiredService<ApiClientCreator>()
            .CreateAsync("Dead webhook client", CancellationToken.None);
        var registered = await scope.ServiceProvider
            .GetRequiredService<WebhookEndpointRegistrar>()
            .RegisterAsync(
                created.ApiClientId,
                "https://93.184.216.34/webhooks?token=sensitive-endpoint-token",
                CancellationToken.None);
        Assert.NotNull(registered);
        Assert.NotNull(registered.WebhookSecret);

        var intake = scope.ServiceProvider.GetRequiredService<MessageIntake>();
        var message = await intake.CreateAsync(
            created.ApiClientId,
            new CreateMessageCommand(
                Type: "transactional",
                Channel: "sms",
                SenderTitle: "NotifyRail",
                Body: "Secret message body",
                Recipients: ["+905551111111"],
                IdempotencyKey: $"dead-webhook-{Guid.NewGuid()}",
                ScheduledAt: null,
                ReportLabel: null,
                Encoding: null),
            CancellationToken.None);
        Assert.NotNull(message.Response);

        var deliveryQueue = scope.ServiceProvider.GetRequiredService<DeliveryQueue>();
        var deliveryJob = Assert.Single(await deliveryQueue.ClaimDueAsync(
            "delivery-worker",
            1,
            DateTimeOffset.UtcNow,
            CancellationToken.None));
        var occurredAt = TruncateToMicroseconds(DateTimeOffset.UtcNow);
        await deliveryQueue.RecordProviderResultAsync(
            deliveryJob.Claim,
            new ProviderResult(
                ProviderOutcome.Accepted,
                Provider: "mock",
                ProviderMessageId: "dead-webhook-provider-id"),
            occurredAt,
            CancellationToken.None);

        var webhookQueue = scope.ServiceProvider.GetRequiredService<WebhookQueue>();
        var webhookJob = Assert.Single(await webhookQueue.ClaimDueAsync(
            "webhook-worker",
            1,
            occurredAt,
            CancellationToken.None));
        await webhookQueue.RecordResultAsync(
            webhookJob.Claim,
            new WebhookResult(
                WebhookOutcome.PermanentFailure,
                HttpStatusCode: 400,
                ErrorCode: "http_error",
                ErrorMessage: "Webhook endpoint returned a permanent HTTP error.",
                LatencyMilliseconds: 3),
            occurredAt,
            occurredAt.AddMilliseconds(3),
            CancellationToken.None);

        var deliveryId = await scope.ServiceProvider
            .GetRequiredService<NotifyRailDbContext>()
            .WebhookEvents
            .Where(webhookEvent => webhookEvent.Id == webhookJob.Request.EventId)
            .Select(webhookEvent => webhookEvent.DeliveryId)
            .SingleAsync();
        return new DeadEventFixture(
            webhookJob.Request.EventId,
            created.ApiClientId,
            deliveryId,
            registered.WebhookSecret,
            created.ApiKey);
    }

    private async Task CreatePendingWebhookEventAsync(Guid apiClientId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var intake = scope.ServiceProvider.GetRequiredService<MessageIntake>();
        var message = await intake.CreateAsync(
            apiClientId,
            new CreateMessageCommand(
                Type: "transactional",
                Channel: "sms",
                SenderTitle: "NotifyRail",
                Body: "Another message",
                Recipients: ["+905552222222"],
                IdempotencyKey: $"pending-webhook-{Guid.NewGuid()}",
                ScheduledAt: null,
                ReportLabel: null,
                Encoding: null),
            CancellationToken.None);
        Assert.NotNull(message.Response);

        var deliveryQueue = scope.ServiceProvider.GetRequiredService<DeliveryQueue>();
        var deliveryJob = Assert.Single(await deliveryQueue.ClaimDueAsync(
            "delivery-worker",
            1,
            DateTimeOffset.UtcNow,
            CancellationToken.None));
        await deliveryQueue.RecordProviderResultAsync(
            deliveryJob.Claim,
            new ProviderResult(
                ProviderOutcome.Accepted,
                Provider: "mock",
                ProviderMessageId: "pending-webhook-provider-id"),
            TruncateToMicroseconds(DateTimeOffset.UtcNow),
            CancellationToken.None);
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

    private async Task<ReplayState> LoadReplayStateAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
        return await dbContext.Database.SqlQueryRaw<ReplayState>(
            """
            SELECT
                webhook_events.id AS "EventId",
                webhook_events.version AS "Version",
                webhook_events.sequence AS "Sequence",
                webhook_events.occurred_at AS "OccurredAt",
                webhook_events.payload AS "Payload",
                webhook_events.status AS "EventStatus",
                webhook_events.automatic_attempt_deadline_at AS "AutomaticAttemptDeadlineAt",
                deliveries.status AS "DeliveryStatus",
                deliveries.updated_at AS "DeliveryUpdatedAt",
                (SELECT count(*)::int FROM webhook_events) AS "EventCount",
                (SELECT count(*)::int FROM webhook_attempts) AS "AttemptCount"
            FROM webhook_events
            JOIN deliveries ON deliveries.id = webhook_events.delivery_id
            """).SingleAsync();
    }

    private async Task<int> ProcessWebhookBatchAsync(
        HttpMessageHandler handler,
        string workerId,
        DateTimeOffset now)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        using var httpClient = new HttpClient(handler, disposeHandler: false);
        var options = Options.Create(new WebhookWorkerOptions
        {
            WorkerId = workerId,
        });
        var worker = new WebhookWorker(
            new WebhookQueue(
                scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>(),
                options,
                scope.ServiceProvider.GetRequiredService<IWebhookRetryJitter>()),
            new WebhookDispatcher(
                httpClient,
                scope.ServiceProvider.GetRequiredService<IWebhookSecretProtector>()),
            options,
            new FixedTimeProvider(now));
        return await worker.ProcessBatchAsync(now, CancellationToken.None);
    }

    private async Task<EventDispatchState> LoadEventDispatchStateAsync(Guid eventId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
        return await dbContext.Database.SqlQuery<EventDispatchState>(
            $"""
            SELECT
                webhook_events.status AS "Status",
                webhook_events.attempt_count AS "AttemptCount",
                last_attempt.outcome AS "LastAttemptOutcome",
                last_attempt.http_status_code AS "LastHttpStatusCode",
                deliveries.status AS "DeliveryStatus"
            FROM webhook_events
            JOIN deliveries ON deliveries.id = webhook_events.delivery_id
            JOIN LATERAL (
                SELECT outcome, http_status_code
                FROM webhook_attempts
                WHERE webhook_event_id = webhook_events.id
                ORDER BY attempt_number DESC
                LIMIT 1
            ) AS last_attempt ON true
            WHERE webhook_events.id = {eventId}
            """).SingleAsync();
    }

    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value)
    {
        return value.AddTicks(-(value.Ticks % 10));
    }

    private sealed record DeadEventFixture(
        Guid EventId,
        Guid ApiClientId,
        Guid DeliveryId,
        string WebhookSecret,
        string ApiKey);

    private sealed class ReplayState
    {
        public Guid EventId { get; init; }
        public int Version { get; init; }
        public int Sequence { get; init; }
        public DateTimeOffset OccurredAt { get; init; }
        public string Payload { get; init; } = null!;
        public string EventStatus { get; init; } = null!;
        public DateTimeOffset? AutomaticAttemptDeadlineAt { get; init; }
        public string DeliveryStatus { get; init; } = null!;
        public DateTimeOffset DeliveryUpdatedAt { get; init; }
        public int EventCount { get; init; }
        public int AttemptCount { get; init; }
    }

    private sealed class EventDispatchState
    {
        public string Status { get; init; } = null!;
        public int AttemptCount { get; init; }
        public string LastAttemptOutcome { get; init; } = null!;
        public int? LastHttpStatusCode { get; init; }
        public string DeliveryStatus { get; init; } = null!;
    }

    private sealed class RecordingWebhookHandler(HttpStatusCode statusCode)
        : HttpMessageHandler
    {
        public string? EventId { get; private set; }
        public string? Body { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            EventId = request.Headers.GetValues("X-NotifyRail-Event-Id").Single();
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(statusCode);
        }
    }

    private sealed class SequenceAwareWebhookHandler : HttpMessageHandler
    {
        private readonly List<int> _rejectedSequences = [];

        public int HighestSequence { get; private set; }
        public IReadOnlyList<int> RejectedSequences => _rejectedSequences;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            var body = await request.Content!.ReadAsStringAsync(cancellationToken);
            using var document = JsonDocument.Parse(body);
            var sequence = document.RootElement
                .GetProperty("data")
                .GetProperty("sequence")
                .GetInt32();
            if (sequence <= HighestSequence)
            {
                _rejectedSequences.Add(sequence);
                return new HttpResponseMessage(HttpStatusCode.Conflict);
            }

            HighestSequence = sequence;
            return new HttpResponseMessage(HttpStatusCode.NoContent);
        }
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
