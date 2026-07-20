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
using Microsoft.Extensions.Options;
using NotifyRail.Api.Features.ApiClients.CreateApiClient;
using NotifyRail.Api.Features.Deliveries.Queue;
using NotifyRail.Api.Features.Messages.CreateMessage;
using NotifyRail.Api.Features.Webhooks.RegisterWebhookEndpoint;
using NotifyRail.Api.Features.Webhooks.RotateWebhookSecret;
using NotifyRail.Api.Features.Webhooks.Dispatch;
using NotifyRail.Api.Features.Webhooks.Queue;
using NotifyRail.Api.Features.Webhooks.Persistence;
using NotifyRail.Api.Features.Webhooks.Worker;
using NotifyRail.Api.Features.Webhooks.Secrets;
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
    public async Task ProcessBatchAsync_UsesOnlyNewSecretAfterRotation()
    {
        await ResetDatabaseAsync();
        await using var receiver = await TestWebhookReceiver.StartAsync(HttpStatusCode.NoContent);
        var (apiClientId, oldSecret) = await CreateApiClientWithEndpointAsync(receiver.Url);
        await CreateMessageAsync(apiClientId);
        await RecordAcceptedDeliveryAsync();

        string newSecret;
        await using (var rotationScope = _factory.Services.CreateAsyncScope())
        {
            var rotation = await rotationScope.ServiceProvider
                .GetRequiredService<WebhookSecretRotator>()
                .RotateAsync(apiClientId, CancellationToken.None);
            Assert.NotNull(rotation);
            newSecret = rotation.WebhookSecret;
        }

        await using var workerScope = _factory.Services.CreateAsyncScope();
        var worker = workerScope.ServiceProvider.GetRequiredService<WebhookWorker>();
        Assert.Equal(1, await worker.ProcessBatchAsync(
            TruncateToMicroseconds(DateTimeOffset.UtcNow),
            CancellationToken.None));

        var received = await receiver.Received.WaitAsync(TimeSpan.FromSeconds(3));
        Assert.Equal(Sign(newSecret, received.Timestamp, received.Body), received.Signature);
        Assert.NotEqual(Sign(oldSecret, received.Timestamp, received.Body), received.Signature);
    }

    [Fact]
    public async Task ProcessBatchAsync_SchedulesServerFailureAndRecordsBoundedRetryableAttempt()
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
        Assert.Equal("retry_scheduled", state.EventStatus);
        Assert.NotNull(state.NextAttemptAt);
        Assert.True(state.NextAttemptAt > state.CompletedAt);
        Assert.Equal("retryable_failure", state.AttemptOutcome);
        Assert.Equal(500, state.HttpStatusCode);
        Assert.Equal("http_error", state.ErrorCode);
        Assert.NotNull(state.ErrorMessage);
        Assert.True(state.ErrorMessage.Length <= 500);
        Assert.DoesNotContain(remoteBody, state.ErrorMessage, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(HttpStatusCode.MovedPermanently, "failed", "permanent_failure")]
    [InlineData(HttpStatusCode.BadRequest, "failed", "permanent_failure")]
    [InlineData(HttpStatusCode.RequestTimeout, "retry_scheduled", "retryable_failure")]
    [InlineData(HttpStatusCode.TooManyRequests, "retry_scheduled", "retryable_failure")]
    [InlineData(HttpStatusCode.ServiceUnavailable, "retry_scheduled", "retryable_failure")]
    public async Task ProcessBatchAsync_ClassifiesHttpFailure(
        HttpStatusCode statusCode,
        string expectedEventStatus,
        string expectedAttemptOutcome)
    {
        await ResetDatabaseAsync();
        await using var receiver = await TestWebhookReceiver.StartAsync(statusCode);
        var (apiClientId, _) = await CreateApiClientWithEndpointAsync(receiver.Url);
        await CreateMessageAsync(apiClientId);
        await RecordAcceptedDeliveryAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var worker = scope.ServiceProvider.GetRequiredService<WebhookWorker>();
        var processed = await worker.ProcessBatchAsync(
            TruncateToMicroseconds(DateTimeOffset.UtcNow),
            CancellationToken.None);

        var state = await LoadWebhookStateAsync();
        Assert.Equal(1, processed);
        Assert.Equal(expectedEventStatus, state.EventStatus);
        Assert.Equal(expectedAttemptOutcome, state.AttemptOutcome);
        Assert.Equal((int)statusCode, state.HttpStatusCode);
        Assert.Equal(expectedEventStatus == "retry_scheduled", state.NextAttemptAt.HasValue);
    }

    [Fact]
    public async Task ProcessBatchAsync_DoesNotFollowRedirects()
    {
        await ResetDatabaseAsync();
        await using var destination = await TestWebhookReceiver.StartAsync(
            HttpStatusCode.NoContent);
        await using var redirectingEndpoint = await TestWebhookReceiver.StartAsync(
            HttpStatusCode.TemporaryRedirect,
            redirectLocation: destination.Url);
        var (apiClientId, _) = await CreateApiClientWithEndpointAsync(redirectingEndpoint.Url);
        await CreateMessageAsync(apiClientId);
        await RecordAcceptedDeliveryAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var worker = scope.ServiceProvider.GetRequiredService<WebhookWorker>();
        var processed = await worker.ProcessBatchAsync(
            TruncateToMicroseconds(DateTimeOffset.UtcNow),
            CancellationToken.None);

        var state = await LoadWebhookStateAsync();
        Assert.Equal(1, processed);
        Assert.Equal(1, redirectingEndpoint.ReceivedCount);
        Assert.Equal(0, destination.ReceivedCount);
        Assert.Equal("failed", state.EventStatus);
        Assert.Equal("permanent_failure", state.AttemptOutcome);
        Assert.Equal((int)HttpStatusCode.TemporaryRedirect, state.HttpStatusCode);
    }

    [Fact]
    public async Task ProcessBatchAsync_SchedulesConfiguredBackoffWithInjectedJitter()
    {
        await ResetDatabaseAsync();
        await using var receiver = await TestWebhookReceiver.StartAsync(
            HttpStatusCode.ServiceUnavailable);
        var (apiClientId, _) = await CreateApiClientWithEndpointAsync(receiver.Url);
        await CreateMessageAsync(apiClientId);
        await RecordAcceptedDeliveryAsync();
        var attemptedAt = TruncateToMicroseconds(DateTimeOffset.UtcNow);
        var completedAt = attemptedAt.AddSeconds(1);
        var options = Options.Create(new WebhookWorkerOptions
        {
            WorkerId = "deterministic-retry-worker",
            BaseRetryDelay = TimeSpan.FromSeconds(10),
            MinimumRetryDelay = TimeSpan.FromSeconds(1),
            MaximumRetryDelay = TimeSpan.FromMinutes(1),
            JitterRatio = 0.25,
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        var queue = new WebhookQueue(
            scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>(),
            options,
            new FixedWebhookRetryJitter(1));
        var worker = new WebhookWorker(
            queue,
            scope.ServiceProvider.GetRequiredService<WebhookDispatcher>(),
            options,
            new SequenceTimeProvider(attemptedAt, completedAt));

        var processed = await worker.ProcessBatchAsync(attemptedAt, CancellationToken.None);

        var state = await LoadWebhookStateAsync();
        Assert.Equal(1, processed);
        Assert.Equal(completedAt.AddSeconds(12.5), state.NextAttemptAt);
    }

    [Theory]
    [InlineData("120", 30)]
    [InlineData("12", 12)]
    [InlineData("0", 2)]
    [InlineData("invalid", 10)]
    public async Task ProcessBatchAsync_HonorsValidRetryAfterWithinConfiguredSafetyBounds(
        string retryAfter,
        int expectedDelaySeconds)
    {
        await ResetDatabaseAsync();
        await using var receiver = await TestWebhookReceiver.StartAsync(
            HttpStatusCode.TooManyRequests,
            retryAfter: retryAfter);
        var (apiClientId, _) = await CreateApiClientWithEndpointAsync(receiver.Url);
        await CreateMessageAsync(apiClientId);
        await RecordAcceptedDeliveryAsync();
        var attemptedAt = TruncateToMicroseconds(DateTimeOffset.UtcNow);
        var completedAt = attemptedAt.AddSeconds(1);
        var options = Options.Create(new WebhookWorkerOptions
        {
            WorkerId = "retry-after-worker",
            BaseRetryDelay = TimeSpan.FromSeconds(10),
            MinimumRetryDelay = TimeSpan.FromSeconds(2),
            MaximumRetryDelay = TimeSpan.FromSeconds(30),
            JitterRatio = 0,
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        var queue = new WebhookQueue(
            scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>(),
            options,
            new FixedWebhookRetryJitter(0.5));
        var worker = new WebhookWorker(
            queue,
            scope.ServiceProvider.GetRequiredService<WebhookDispatcher>(),
            options,
            new SequenceTimeProvider(attemptedAt, completedAt));

        await worker.ProcessBatchAsync(attemptedAt, CancellationToken.None);

        var state = await LoadWebhookStateAsync();
        Assert.Equal(completedAt.AddSeconds(expectedDelaySeconds), state.NextAttemptAt);
    }

    [Fact]
    public async Task ProcessBatchAsync_HonorsAbsoluteRetryAfterWithoutAddingRequestLatency()
    {
        await ResetDatabaseAsync();
        var attemptedAt = DateTimeOffset.FromUnixTimeSeconds(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
        var completedAt = attemptedAt.AddSeconds(5);
        var retryAt = attemptedAt.AddSeconds(20);
        await using var receiver = await TestWebhookReceiver.StartAsync(
            HttpStatusCode.ServiceUnavailable,
            retryAfter: retryAt.ToString("R"));
        var (apiClientId, _) = await CreateApiClientWithEndpointAsync(receiver.Url);
        await CreateMessageAsync(apiClientId);
        await RecordAcceptedDeliveryAsync();
        var options = Options.Create(new WebhookWorkerOptions
        {
            WorkerId = "absolute-retry-after-worker",
            BaseRetryDelay = TimeSpan.FromSeconds(10),
            MinimumRetryDelay = TimeSpan.FromSeconds(1),
            MaximumRetryDelay = TimeSpan.FromMinutes(1),
            JitterRatio = 0,
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        var queue = new WebhookQueue(
            scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>(),
            options,
            new FixedWebhookRetryJitter(0.5));
        var worker = new WebhookWorker(
            queue,
            scope.ServiceProvider.GetRequiredService<WebhookDispatcher>(),
            options,
            new SequenceTimeProvider(attemptedAt, completedAt));

        await worker.ProcessBatchAsync(attemptedAt, CancellationToken.None);

        var state = await LoadWebhookStateAsync();
        Assert.Equal(retryAt, state.NextAttemptAt);
    }

    [Fact]
    public async Task ProcessBatchAsync_DoublesBackoffAndDoesNotClaimBeforeDueTime()
    {
        await ResetDatabaseAsync();
        await using var receiver = await TestWebhookReceiver.StartAsync(
            HttpStatusCode.ServiceUnavailable);
        var (apiClientId, _) = await CreateApiClientWithEndpointAsync(receiver.Url);
        await CreateMessageAsync(apiClientId);
        await RecordAcceptedDeliveryAsync();
        var firstAttemptedAt = TruncateToMicroseconds(DateTimeOffset.UtcNow);
        var firstCompletedAt = firstAttemptedAt.AddSeconds(1);
        var options = Options.Create(new WebhookWorkerOptions
        {
            WorkerId = "exponential-retry-worker",
            BaseRetryDelay = TimeSpan.FromSeconds(10),
            MinimumRetryDelay = TimeSpan.FromSeconds(1),
            MaximumRetryDelay = TimeSpan.FromMinutes(1),
            JitterRatio = 0,
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        var queue = new WebhookQueue(
            scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>(),
            options,
            new FixedWebhookRetryJitter(0.5));
        var firstWorker = new WebhookWorker(
            queue,
            scope.ServiceProvider.GetRequiredService<WebhookDispatcher>(),
            options,
            new SequenceTimeProvider(firstAttemptedAt, firstCompletedAt));

        await firstWorker.ProcessBatchAsync(firstAttemptedAt, CancellationToken.None);

        var firstState = await LoadWebhookEventStateAsync();
        var firstDueAt = Assert.IsType<DateTimeOffset>(firstState.NextAttemptAt);
        Assert.Equal(firstCompletedAt.AddSeconds(10), firstDueAt);
        Assert.Empty(await queue.ClaimDueAsync(
            "too-early-worker",
            1,
            firstDueAt.AddTicks(-1),
            CancellationToken.None));

        var secondCompletedAt = firstDueAt.AddSeconds(1);
        var secondWorker = new WebhookWorker(
            queue,
            scope.ServiceProvider.GetRequiredService<WebhookDispatcher>(),
            options,
            new SequenceTimeProvider(firstDueAt, secondCompletedAt));
        var processed = await secondWorker.ProcessBatchAsync(firstDueAt, CancellationToken.None);

        var secondState = await LoadWebhookEventStateAsync();
        Assert.Equal(1, processed);
        Assert.Equal(2, secondState.AttemptCount);
        Assert.Equal(secondCompletedAt.AddSeconds(20), secondState.NextAttemptAt);
    }

    [Fact]
    public async Task ClaimDueAsync_RecoversClaimAfterConfiguredTimeout()
    {
        await ResetDatabaseAsync();
        var (apiClientId, _) = await CreateApiClientWithEndpointAsync("http://127.0.0.1:1/webhooks");
        await CreateMessageAsync(apiClientId);
        await RecordAcceptedDeliveryAsync();
        var firstClaimTime = TruncateToMicroseconds(DateTimeOffset.UtcNow);
        var options = Options.Create(new WebhookWorkerOptions
        {
            WorkerId = "stale-claim-worker",
            ClaimTimeout = TimeSpan.FromSeconds(30),
            RequestTimeout = TimeSpan.FromSeconds(10),
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        var queue = new WebhookQueue(
            scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>(),
            options,
            new FixedWebhookRetryJitter(0.5));

        var firstJob = Assert.Single(await queue.ClaimDueAsync(
            "worker-a",
            1,
            firstClaimTime,
            CancellationToken.None));
        Assert.Empty(await queue.ClaimDueAsync(
            "worker-b",
            1,
            firstClaimTime.AddSeconds(29),
            CancellationToken.None));

        var recoveredJob = Assert.Single(await queue.ClaimDueAsync(
            "worker-b",
            1,
            firstClaimTime.AddSeconds(30),
            CancellationToken.None));

        Assert.Equal(firstJob.Request.EventId, recoveredJob.Request.EventId);
        Assert.Equal(firstJob.Request.Body, recoveredJob.Request.Body);
        Assert.Equal(1, recoveredJob.Claim.AttemptNumber);
    }

    [Fact]
    public async Task ClaimDueAsync_SkipsLockedStaleClaimAndClaimsUnrelatedDelivery()
    {
        await ResetDatabaseAsync();
        var (apiClientId, _) = await CreateApiClientWithEndpointAsync("http://127.0.0.1:1/webhooks");
        await CreateMessageAsync(apiClientId, ["+905551111111", "+905552222222"]);
        await RecordAcceptedDeliveriesAsync(count: 2);
        var firstClaimTime = TruncateToMicroseconds(DateTimeOffset.UtcNow);
        var options = Options.Create(new WebhookWorkerOptions
        {
            WorkerId = "skip-locked-worker",
            ClaimTimeout = TimeSpan.FromSeconds(30),
            RequestTimeout = TimeSpan.FromSeconds(10),
        });

        await using var firstScope = _factory.Services.CreateAsyncScope();
        var firstQueue = new WebhookQueue(
            firstScope.ServiceProvider.GetRequiredService<NotifyRailDbContext>(),
            options,
            new FixedWebhookRetryJitter(0.5));
        var staleJob = Assert.Single(await firstQueue.ClaimDueAsync(
            "worker-a",
            1,
            firstClaimTime,
            CancellationToken.None));

        await using var lockScope = _factory.Services.CreateAsyncScope();
        var lockContext = lockScope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
        await using var lockTransaction =
            await lockContext.Database.BeginTransactionAsync(CancellationToken.None);
        await lockContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT id FROM webhook_events WHERE id = {staleJob.Request.EventId} FOR UPDATE");

        await using var claimScope = _factory.Services.CreateAsyncScope();
        var claimContext = claimScope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
        claimContext.Database.SetCommandTimeout(TimeSpan.FromSeconds(1));
        var queue = new WebhookQueue(
            claimContext,
            options,
            new FixedWebhookRetryJitter(0.5));

        var claimed = Assert.Single(await queue.ClaimDueAsync(
            "worker-b",
            1,
            firstClaimTime.AddSeconds(30),
            CancellationToken.None));

        Assert.NotEqual(staleJob.Request.EventId, claimed.Request.EventId);
    }

    [Fact]
    public async Task ProcessBatchAsync_SchedulesTimeoutAndRecordsNormalizedAttempt()
    {
        await ResetDatabaseAsync();
        await using var receiver = await TestWebhookReceiver.StartAsync(
            HttpStatusCode.NoContent,
            responseDelay: TimeSpan.FromSeconds(1));
        var (apiClientId, _) = await CreateApiClientWithEndpointAsync(receiver.Url);
        await CreateMessageAsync(apiClientId);
        await RecordAcceptedDeliveryAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        using var httpClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
        })
        {
            Timeout = TimeSpan.FromMilliseconds(50),
        };
        var dispatcher = new WebhookDispatcher(
            httpClient,
            scope.ServiceProvider.GetRequiredService<IWebhookSecretProtector>());
        var worker = new WebhookWorker(
            scope.ServiceProvider.GetRequiredService<WebhookQueue>(),
            dispatcher,
            Options.Create(new WebhookWorkerOptions { WorkerId = "timeout-worker" }),
            TimeProvider.System);

        var processed = await worker.ProcessBatchAsync(
            TruncateToMicroseconds(DateTimeOffset.UtcNow),
            CancellationToken.None);

        var state = await LoadWebhookStateAsync();
        Assert.Equal(1, processed);
        Assert.Equal("retry_scheduled", state.EventStatus);
        Assert.Equal("retryable_failure", state.AttemptOutcome);
        Assert.Null(state.HttpStatusCode);
        Assert.Equal("timeout", state.ErrorCode);
        Assert.Equal("Webhook request timed out.", state.ErrorMessage);
        Assert.True(state.LatencyMilliseconds >= 0);
    }

    [Fact]
    public void Startup_RejectsRetryDelayOutsideConfiguredBounds()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("WebhookWorker:BaseRetryDelay", "00:01:00");
            builder.UseSetting("WebhookWorker:MaximumRetryDelay", "00:00:30");
        });

        var exception = Assert.Throws<OptionsValidationException>(factory.CreateClient);

        Assert.Contains(
            "MaximumRetryDelay must not be less than BaseRetryDelay",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_RejectsNonPositiveClaimTimeout()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("WebhookWorker:ClaimTimeout", "00:00:00");
        });

        var exception = Assert.Throws<OptionsValidationException>(factory.CreateClient);

        Assert.Contains(
            "ClaimTimeout must be greater than zero",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Startup_RejectsClaimTimeoutNotLongerThanRequestTimeout()
    {
        using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.UseSetting("WebhookWorker:RequestTimeout", "00:00:02");
            builder.UseSetting("WebhookWorker:ClaimTimeout", "00:00:01");
        });

        var exception = Assert.Throws<OptionsValidationException>(factory.CreateClient);

        Assert.Contains(
            "ClaimTimeout must be greater than RequestTimeout",
            exception.Message,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessBatchAsync_SchedulesNetworkErrorWithoutPersistingEndpointDetail()
    {
        await ResetDatabaseAsync();
        const string unreachableEndpoint = "http://127.0.0.1:1/webhooks?token=secret";
        var (apiClientId, _) = await CreateApiClientWithEndpointAsync(unreachableEndpoint);
        await CreateMessageAsync(apiClientId);
        await RecordAcceptedDeliveryAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var worker = scope.ServiceProvider.GetRequiredService<WebhookWorker>();
        var processed = await worker.ProcessBatchAsync(
            TruncateToMicroseconds(DateTimeOffset.UtcNow),
            CancellationToken.None);

        var state = await LoadWebhookStateAsync();
        Assert.Equal(1, processed);
        Assert.Equal("retry_scheduled", state.EventStatus);
        Assert.Equal("retryable_failure", state.AttemptOutcome);
        Assert.Null(state.HttpStatusCode);
        Assert.Equal("network_error", state.ErrorCode);
        Assert.Equal(
            "Webhook request failed before a response was received.",
            state.ErrorMessage);
        Assert.DoesNotContain("secret", state.ErrorMessage, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ProcessBatchAsync_RecordsAttemptWhenCancellationFollowsOutboundRequest()
    {
        await ResetDatabaseAsync();
        await using var receiver = await TestWebhookReceiver.StartAsync(
            HttpStatusCode.NoContent,
            responseDelay: TimeSpan.FromSeconds(1));
        var (apiClientId, _) = await CreateApiClientWithEndpointAsync(receiver.Url);
        await CreateMessageAsync(apiClientId);
        await RecordAcceptedDeliveryAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var worker = scope.ServiceProvider.GetRequiredService<WebhookWorker>();
        using var cancellation = new CancellationTokenSource();
        var processing = worker.ProcessBatchAsync(
            TruncateToMicroseconds(DateTimeOffset.UtcNow),
            cancellation.Token);
        await receiver.Received.WaitAsync(TimeSpan.FromSeconds(3));

        cancellation.Cancel();
        var processed = await processing;

        var state = await LoadWebhookStateAsync();
        Assert.Equal(1, processed);
        Assert.Equal("retry_scheduled", state.EventStatus);
        Assert.Equal("retryable_failure", state.AttemptOutcome);
        Assert.Null(state.HttpStatusCode);
        Assert.Equal("request_canceled", state.ErrorCode);
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
            timeProvider);

        var processed = await worker.ProcessBatchAsync(startedAt, CancellationToken.None);

        var received = await receiver.WaitForCountAsync(2);
        Assert.Equal(2, processed);
        Assert.Equal(2, received.Count);
        Assert.NotEqual(received[0].Timestamp, received[1].Timestamp);
        Assert.True(long.Parse(received[1].Timestamp) > long.Parse(received[0].Timestamp));
    }

    [Fact]
    public async Task ClaimDueAsync_ConcurrentWorkersClaimDifferentDeliveriesOnce()
    {
        await ResetDatabaseAsync();
        var (apiClientId, _) = await CreateApiClientWithEndpointAsync("http://127.0.0.1:1/webhooks");
        await CreateMessageAsync(apiClientId, ["+905551111111", "+905552222222"]);
        await RecordAcceptedDeliveriesAsync(count: 2);
        var claimTime = TruncateToMicroseconds(DateTimeOffset.UtcNow);

        await using var firstScope = _factory.Services.CreateAsyncScope();
        await using var secondScope = _factory.Services.CreateAsyncScope();
        var firstQueue = firstScope.ServiceProvider.GetRequiredService<WebhookQueue>();
        var secondQueue = secondScope.ServiceProvider.GetRequiredService<WebhookQueue>();

        var claims = await Task.WhenAll(
            firstQueue.ClaimDueAsync("concurrent-worker-a", 1, claimTime, CancellationToken.None),
            secondQueue.ClaimDueAsync("concurrent-worker-b", 1, claimTime, CancellationToken.None));

        var jobs = claims.SelectMany(batch => batch).ToArray();
        Assert.Equal(2, jobs.Length);
        Assert.Equal(2, jobs.Select(job => job.Request.EventId).Distinct().Count());
        Assert.Equal(2, jobs.Select(job => job.Claim.WorkerId).Distinct().Count());
    }

    [Fact]
    public async Task ClaimDueAsync_WaitsForConcurrentRotationAndSelectsCommittedSecret()
    {
        await ResetDatabaseAsync();
        var (apiClientId, oldSecret) = await CreateApiClientWithEndpointAsync(
            "http://127.0.0.1:1/webhooks");
        await CreateMessageAsync(apiClientId);
        await RecordAcceptedDeliveryAsync();
        var rotatedAt = TruncateToMicroseconds(DateTimeOffset.UtcNow);
        const string newSecret = "nrs_concurrent-rotation-secret";

        await using var rotationScope = _factory.Services.CreateAsyncScope();
        var rotationContext = rotationScope.ServiceProvider
            .GetRequiredService<NotifyRailDbContext>();
        var protector = rotationScope.ServiceProvider
            .GetRequiredService<IWebhookSecretProtector>();
        await using var rotationTransaction =
            await rotationContext.Database.BeginTransactionAsync();
        await rotationContext.ApiClients
            .FromSqlInterpolated(
                $"SELECT * FROM api_clients WHERE id = {apiClientId} FOR UPDATE")
            .SingleAsync();
        var oldPersistedSecret = await rotationContext.WebhookSecrets.SingleAsync(
            secret => secret.ApiClientId == apiClientId && secret.RetiredAt == null);
        oldPersistedSecret.Retire(rotatedAt.AddHours(24));
        rotationContext.WebhookSecrets.Add(WebhookSecret.Create(
            apiClientId,
            protector.Protect(newSecret),
            rotatedAt));
        await rotationContext.SaveChangesAsync();

        await using var claimScope = _factory.Services.CreateAsyncScope();
        var queue = claimScope.ServiceProvider.GetRequiredService<WebhookQueue>();
        var claiming = queue.ClaimDueAsync(
            "rotation-race-worker",
            1,
            rotatedAt,
            CancellationToken.None);

        await Task.Delay(TimeSpan.FromMilliseconds(150));
        Assert.False(claiming.IsCompleted);

        await rotationTransaction.CommitAsync();
        var job = Assert.Single(await claiming.WaitAsync(TimeSpan.FromSeconds(3)));
        Assert.Equal(newSecret, protector.Unprotect(job.Request.ProtectedSecret));
        Assert.NotEqual(oldSecret, protector.Unprotect(job.Request.ProtectedSecret));
    }

    [Fact]
    public async Task ProcessBatchAsync_ConcurrentWorkersSendSingleEventOnce()
    {
        await ResetDatabaseAsync();
        await using var receiver = await TestWebhookReceiver.StartAsync(
            HttpStatusCode.NoContent,
            responseDelay: TimeSpan.FromMilliseconds(250));
        var (apiClientId, _) = await CreateApiClientWithEndpointAsync(receiver.Url);
        await CreateMessageAsync(apiClientId);
        await RecordAcceptedDeliveryAsync();
        var dispatchAt = TruncateToMicroseconds(DateTimeOffset.UtcNow);

        await using var firstScope = _factory.Services.CreateAsyncScope();
        await using var secondScope = _factory.Services.CreateAsyncScope();
        var firstWorker = new WebhookWorker(
            firstScope.ServiceProvider.GetRequiredService<WebhookQueue>(),
            firstScope.ServiceProvider.GetRequiredService<WebhookDispatcher>(),
            Options.Create(new WebhookWorkerOptions { WorkerId = "concurrent-worker-a" }),
            TimeProvider.System);
        var secondWorker = new WebhookWorker(
            secondScope.ServiceProvider.GetRequiredService<WebhookQueue>(),
            secondScope.ServiceProvider.GetRequiredService<WebhookDispatcher>(),
            Options.Create(new WebhookWorkerOptions { WorkerId = "concurrent-worker-b" }),
            TimeProvider.System);

        var processed = await Task.WhenAll(
            firstWorker.ProcessBatchAsync(dispatchAt, CancellationToken.None),
            secondWorker.ProcessBatchAsync(dispatchAt, CancellationToken.None));

        Assert.Equal(1, processed.Sum());
        Assert.Equal(1, receiver.ReceivedCount);
    }

    [Fact]
    public async Task ProcessBatchAsync_ConcurrentWorkersSendDifferentDeliveriesInParallel()
    {
        await ResetDatabaseAsync();
        await using var receiver = await TestWebhookReceiver.StartAsync(
            HttpStatusCode.NoContent,
            responseDelay: TimeSpan.FromMilliseconds(500));
        var (apiClientId, _) = await CreateApiClientWithEndpointAsync(receiver.Url);
        await CreateMessageAsync(apiClientId, ["+905551111111", "+905552222222"]);
        await RecordAcceptedDeliveriesAsync(count: 2);
        var dispatchAt = TruncateToMicroseconds(DateTimeOffset.UtcNow);

        await using var firstScope = _factory.Services.CreateAsyncScope();
        await using var secondScope = _factory.Services.CreateAsyncScope();
        var firstWorker = new WebhookWorker(
            firstScope.ServiceProvider.GetRequiredService<WebhookQueue>(),
            firstScope.ServiceProvider.GetRequiredService<WebhookDispatcher>(),
            Options.Create(new WebhookWorkerOptions { WorkerId = "parallel-worker-a" }),
            TimeProvider.System);
        var secondWorker = new WebhookWorker(
            secondScope.ServiceProvider.GetRequiredService<WebhookQueue>(),
            secondScope.ServiceProvider.GetRequiredService<WebhookDispatcher>(),
            Options.Create(new WebhookWorkerOptions { WorkerId = "parallel-worker-b" }),
            TimeProvider.System);

        var processed = await Task.WhenAll(
            firstWorker.ProcessBatchAsync(dispatchAt, CancellationToken.None),
            secondWorker.ProcessBatchAsync(dispatchAt, CancellationToken.None));

        Assert.Equal(2, processed.Sum());
        Assert.Equal(2, receiver.ReceivedCount);
        Assert.Equal(2, receiver.MaximumConcurrentRequests);
    }

    [Fact]
    public async Task ProcessBatchAsync_CommitsClaimBeforeWaitingForReceiver()
    {
        await ResetDatabaseAsync();
        await using var receiver = await TestWebhookReceiver.StartAsync(
            HttpStatusCode.NoContent,
            responseDelay: TimeSpan.FromSeconds(1));
        var (apiClientId, _) = await CreateApiClientWithEndpointAsync(receiver.Url);
        await CreateMessageAsync(apiClientId);
        await RecordAcceptedDeliveryAsync();

        await using var workerScope = _factory.Services.CreateAsyncScope();
        var worker = workerScope.ServiceProvider.GetRequiredService<WebhookWorker>();
        var processing = worker.ProcessBatchAsync(
            TruncateToMicroseconds(DateTimeOffset.UtcNow),
            CancellationToken.None);
        var received = await receiver.Received.WaitAsync(TimeSpan.FromSeconds(3));

        await using var lockScope = _factory.Services.CreateAsyncScope();
        var lockContext = lockScope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
        await using (var transaction =
            await lockContext.Database.BeginTransactionAsync(CancellationToken.None))
        {
            await lockContext.Database.ExecuteSqlInterpolatedAsync(
                $"SELECT id FROM webhook_events WHERE id = {Guid.Parse(received.EventId)} FOR UPDATE NOWAIT");
        }

        Assert.Equal(1, await processing);
    }

    [Fact]
    public async Task ProcessBatchAsync_RetriesAmbiguousResponseWithSameLogicalEvent()
    {
        await ResetDatabaseAsync();
        await using var receiver = await TestWebhookReceiver.StartAsync(
            HttpStatusCode.NoContent,
            responseDelay: TimeSpan.FromMilliseconds(250));
        var (apiClientId, _) = await CreateApiClientWithEndpointAsync(receiver.Url);
        await CreateMessageAsync(apiClientId);
        await RecordAcceptedDeliveryAsync();
        var firstAttemptedAt = TruncateToMicroseconds(DateTimeOffset.UtcNow);
        var firstCompletedAt = firstAttemptedAt.AddMilliseconds(50);
        var options = Options.Create(new WebhookWorkerOptions
        {
            WorkerId = "ambiguous-response-worker",
            BaseRetryDelay = TimeSpan.FromSeconds(1),
            MinimumRetryDelay = TimeSpan.FromSeconds(1),
            MaximumRetryDelay = TimeSpan.FromSeconds(1),
            JitterRatio = 0,
        });

        await using var scope = _factory.Services.CreateAsyncScope();
        using var timeoutClient = new HttpClient(new HttpClientHandler
        {
            AllowAutoRedirect = false,
        })
        {
            Timeout = TimeSpan.FromMilliseconds(50),
        };
        var queue = new WebhookQueue(
            scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>(),
            options,
            new FixedWebhookRetryJitter(0.5));
        var firstWorker = new WebhookWorker(
            queue,
            new WebhookDispatcher(
                timeoutClient,
                scope.ServiceProvider.GetRequiredService<IWebhookSecretProtector>()),
            options,
            new SequenceTimeProvider(firstAttemptedAt, firstCompletedAt));

        Assert.Equal(1, await firstWorker.ProcessBatchAsync(
            firstAttemptedAt,
            CancellationToken.None));
        var retryAt = Assert.IsType<DateTimeOffset>((await LoadWebhookEventStateAsync()).NextAttemptAt);
        var secondWorker = new WebhookWorker(
            queue,
            scope.ServiceProvider.GetRequiredService<WebhookDispatcher>(),
            options,
            new SequenceTimeProvider(retryAt, retryAt.AddMilliseconds(250)));

        Assert.Equal(1, await secondWorker.ProcessBatchAsync(retryAt, CancellationToken.None));

        var received = await receiver.WaitForCountAsync(2);
        var state = await LoadAmbiguousDispatchStateAsync();
        Assert.Equal(received[0].EventId, received[1].EventId);
        Assert.Equal(received[0].Body, received[1].Body);
        Assert.Equal(1, state.EventCount);
        Assert.Equal(2, state.AttemptCount);
        Assert.Equal("retryable_failure", state.FirstOutcome);
        Assert.Equal("succeeded", state.SecondOutcome);
        Assert.Equal("succeeded", state.EventStatus);
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
                webhook_events.next_attempt_at AS "NextAttemptAt",
                webhook_events.succeeded_at AS "EventSucceededAt",
                webhook_attempts.attempt_number AS "AttemptNumber",
                webhook_attempts.outcome AS "AttemptOutcome",
                webhook_attempts.http_status_code AS "HttpStatusCode",
                webhook_attempts.error_code AS "ErrorCode",
                webhook_attempts.error_message AS "ErrorMessage",
                webhook_attempts.latency_milliseconds AS "LatencyMilliseconds",
                webhook_attempts.attempted_at AS "AttemptedAt",
                webhook_attempts.completed_at AS "CompletedAt"
            FROM webhook_events
            JOIN deliveries ON deliveries.id = webhook_events.delivery_id
            JOIN webhook_attempts ON webhook_attempts.webhook_event_id = webhook_events.id
            """).SingleAsync(CancellationToken.None);
    }

    private async Task<WebhookEventState> LoadWebhookEventStateAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
        return await dbContext.Database.SqlQueryRaw<WebhookEventState>(
            """
            SELECT
                status AS "Status",
                attempt_count AS "AttemptCount",
                next_attempt_at AS "NextAttemptAt"
            FROM webhook_events
            """).SingleAsync(CancellationToken.None);
    }

    private async Task<AmbiguousDispatchState> LoadAmbiguousDispatchStateAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
        return await dbContext.Database.SqlQueryRaw<AmbiguousDispatchState>(
            """
            SELECT
                (SELECT count(*)::int FROM webhook_events) AS "EventCount",
                (SELECT count(*)::int FROM webhook_attempts) AS "AttemptCount",
                (SELECT outcome FROM webhook_attempts ORDER BY attempt_number LIMIT 1)
                    AS "FirstOutcome",
                (SELECT outcome FROM webhook_attempts ORDER BY attempt_number OFFSET 1 LIMIT 1)
                    AS "SecondOutcome",
                (SELECT status FROM webhook_events) AS "EventStatus"
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
        public DateTimeOffset? NextAttemptAt { get; init; }
        public DateTimeOffset? EventSucceededAt { get; init; }
        public int AttemptNumber { get; init; }
        public string AttemptOutcome { get; init; } = null!;
        public int? HttpStatusCode { get; init; }
        public string? ErrorCode { get; init; }
        public string? ErrorMessage { get; init; }
        public long LatencyMilliseconds { get; init; }
        public DateTimeOffset AttemptedAt { get; init; }
        public DateTimeOffset CompletedAt { get; init; }
    }

    private sealed class WebhookEventState
    {
        public string Status { get; init; } = null!;
        public int AttemptCount { get; init; }
        public DateTimeOffset? NextAttemptAt { get; init; }
    }

    private sealed class AmbiguousDispatchState
    {
        public int EventCount { get; init; }
        public int AttemptCount { get; init; }
        public string FirstOutcome { get; init; } = null!;
        public string SecondOutcome { get; init; } = null!;
        public string EventStatus { get; init; } = null!;
    }

    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value)
    {
        return value.AddTicks(-(value.Ticks % 10));
    }

    private sealed class TestWebhookReceiver : IAsyncDisposable
    {
        private readonly WebApplication _application;
        private readonly ConcurrentQueue<ReceivedWebhook> _receivedWebhooks;
        private readonly RequestConcurrency _requestConcurrency;

        private TestWebhookReceiver(
            WebApplication application,
            string url,
            Task<ReceivedWebhook> received,
            ConcurrentQueue<ReceivedWebhook> receivedWebhooks,
            RequestConcurrency requestConcurrency)
        {
            _application = application;
            Url = url;
            Received = received;
            _receivedWebhooks = receivedWebhooks;
            _requestConcurrency = requestConcurrency;
        }

        public string Url { get; }
        public Task<ReceivedWebhook> Received { get; }
        public int ReceivedCount => _receivedWebhooks.Count;
        public int MaximumConcurrentRequests => _requestConcurrency.Maximum;

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
            string? responseBody = null,
            string? retryAfter = null,
            TimeSpan? responseDelay = null,
            string? redirectLocation = null)
        {
            var received = new TaskCompletionSource<ReceivedWebhook>(
                TaskCreationOptions.RunContinuationsAsynchronously);
            var receivedWebhooks = new ConcurrentQueue<ReceivedWebhook>();
            var requestConcurrency = new RequestConcurrency();
            var builder = WebApplication.CreateSlimBuilder();
            builder.WebHost.UseUrls("http://127.0.0.1:0");
            var application = builder.Build();
            application.MapPost("/webhooks", async context =>
            {
                requestConcurrency.Enter();
                try
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
                    if (responseDelay is { } delay)
                    {
                        await Task.Delay(delay, context.RequestAborted);
                    }
                    context.Response.StatusCode = (int)statusCode;
                    if (retryAfter is not null)
                    {
                        context.Response.Headers.RetryAfter = retryAfter;
                    }
                    if (redirectLocation is not null)
                    {
                        context.Response.Headers.Location = redirectLocation;
                    }
                    if (responseBody is not null)
                    {
                        await context.Response.WriteAsync(responseBody, context.RequestAborted);
                    }
                }
                finally
                {
                    requestConcurrency.Exit();
                }
            });
            await application.StartAsync();
            var address = application.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses.Single();
            return new TestWebhookReceiver(
                application,
                $"{address}/webhooks",
                received.Task,
                receivedWebhooks,
                requestConcurrency);
        }

        public async ValueTask DisposeAsync()
        {
            await _application.StopAsync();
            await _application.DisposeAsync();
        }
    }

    private sealed class RequestConcurrency
    {
        private int _active;
        private int _maximum;

        public int Maximum => Volatile.Read(ref _maximum);

        public void Enter()
        {
            var active = Interlocked.Increment(ref _active);
            var maximum = Volatile.Read(ref _maximum);
            while (active > maximum)
            {
                var observed = Interlocked.CompareExchange(ref _maximum, active, maximum);
                if (observed == maximum)
                {
                    break;
                }

                maximum = observed;
            }
        }

        public void Exit()
        {
            Interlocked.Decrement(ref _active);
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

    private sealed class SequenceTimeProvider(params DateTimeOffset[] values) : TimeProvider
    {
        private int _index;

        public override DateTimeOffset GetUtcNow()
        {
            return values[Math.Min(Interlocked.Increment(ref _index) - 1, values.Length - 1)];
        }
    }

    private sealed class FixedWebhookRetryJitter(double value) : IWebhookRetryJitter
    {
        public double NextUnitIntervalSample()
        {
            return value;
        }
    }
}
