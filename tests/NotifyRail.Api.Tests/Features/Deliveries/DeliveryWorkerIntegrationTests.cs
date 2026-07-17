using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NotifyRail.Api.Features.Deliveries.Providers;
using NotifyRail.Api.Features.Deliveries.Queue;
using NotifyRail.Api.Features.Deliveries.Worker;
using NotifyRail.Api.Features.Messages.CreateMessage;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Tests;

public sealed class DeliveryWorkerIntegrationTests
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static string PostgresConnectionString =>
        Environment.GetEnvironmentVariable("ConnectionStrings__Postgres")
        ?? "Host=localhost;Port=5432;Database=notifyrail;Username=notifyrail;Password=notifyrail";

    private readonly WebApplicationFactory<Program> _hostedFactory;
    private readonly WebApplicationFactory<Program> _manualFactory;

    public DeliveryWorkerIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _hostedFactory = factory.WithMessageApiAuthentication();
        _manualFactory = _hostedFactory.WithoutHostedServices();
    }

    public void Dispose()
    {
        _manualFactory.Dispose();
    }

    [Fact]
    public async Task ProcessBatchAsync_SendsDueDeliveryThroughMockProviderAndRecordsAcceptedResult()
    {
        await ResetDatabaseAsync();
        await CreateMessageAsync();

        await using var scope = _manualFactory.Services.CreateAsyncScope();
        var worker = scope.ServiceProvider.GetRequiredService<DeliveryWorker>();

        var processed = await worker.ProcessBatchAsync(
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        var state = await LoadDeliveryStateAsync();
        Assert.NotNull(state.ProviderMessageId);
        var providerMessageId = state.ProviderMessageId;
        Assert.Equal(1, processed);
        Assert.Equal("sent", state.Status);
        Assert.Equal(1, state.AttemptCount);
        Assert.StartsWith("mock_", providerMessageId);
        Assert.Equal("mock", state.AttemptProvider);
        Assert.Equal("accepted", state.AttemptOutcome);
        Assert.Equal(providerMessageId, state.AttemptProviderMessageId);
    }

    [Fact]
    public async Task ProcessBatchAsync_SchedulesRetry_WhenProviderThrowsTransientException()
    {
        await ResetDatabaseWithoutStartingHostAsync();

        await using var factory = _hostedFactory
            .WithoutHostedServices()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IProviderSender>();
                    services.AddSingleton<IProviderSender, TransientlyFailingProvider>();
                });
            });
        using var client = await factory.CreateAuthenticatedMessageClientAsync(
            "Transient Delivery Worker");
        await CreateMessageAsync(client);

        await using var scope = factory.Services.CreateAsyncScope();
        var worker = scope.ServiceProvider.GetRequiredService<DeliveryWorker>();

        var processed = await worker.ProcessBatchAsync(
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        var state = await LoadDeliveryStateWithoutStartingHostAsync();
        Assert.Equal(1, processed);
        Assert.Equal("retry_scheduled", state.Status);
        Assert.Equal(1, state.AttemptCount);
        Assert.Equal("transient-test", state.AttemptProvider);
        Assert.Equal("retryable_failure", state.AttemptOutcome);
        Assert.Equal("provider_exception", state.AttemptErrorCode);
        Assert.Equal("Provider connection failed.", state.AttemptErrorMessage);
    }

    [Fact]
    public async Task ProcessBatchAsync_SchedulesRetry_WhenProviderTimesOut()
    {
        await ResetDatabaseWithoutStartingHostAsync();

        await using var factory = _hostedFactory
            .WithoutHostedServices()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<IProviderSender>();
                    services.AddSingleton<IProviderSender, TimingOutProvider>();
                });
            });
        using var client = await factory.CreateAuthenticatedMessageClientAsync(
            "Timeout Delivery Worker");
        await CreateMessageAsync(client);

        await using var scope = factory.Services.CreateAsyncScope();
        var worker = scope.ServiceProvider.GetRequiredService<DeliveryWorker>();

        var processed = await worker.ProcessBatchAsync(
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        var state = await LoadDeliveryStateWithoutStartingHostAsync();
        Assert.Equal(1, processed);
        Assert.Equal("retry_scheduled", state.Status);
        Assert.Equal("timeout-test", state.AttemptProvider);
        Assert.Equal("retryable_failure", state.AttemptOutcome);
        Assert.Equal("provider_exception", state.AttemptErrorCode);
        Assert.Equal("Provider timed out.", state.AttemptErrorMessage);
    }

    [Fact]
    public async Task BackgroundService_SendsDueDeliveryWithoutManualBatchCall()
    {
        await ResetDatabaseWithoutStartingHostAsync();

        await using var factory = _hostedFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DeliveryWorker:BatchSize"] = "1",
                    ["DeliveryWorker:PollInterval"] = "00:00:00.050",
                });
            });
        });
        using var client = await factory.CreateAuthenticatedMessageClientAsync(
            "Hosted Delivery Worker");

        await CreateMessageAsync(client);

        var state = await WaitForDeliveryStatusAsync("sent");
        Assert.Equal(1, state.AttemptCount);
        Assert.StartsWith("mock_", state.ProviderMessageId);
        Assert.Equal("mock", state.AttemptProvider);
        Assert.Equal("accepted", state.AttemptOutcome);
    }

    [Fact]
    public async Task BackgroundService_ContinuesPolling_WhenBatchFailsUnexpectedly()
    {
        await ResetDatabaseWithoutStartingHostAsync();
        await CreateMessageAsync();
        await CreateMessageAsync();

        await using var factory = _hostedFactory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureAppConfiguration((_, configuration) =>
            {
                configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DeliveryWorker:BatchSize"] = "1",
                    ["DeliveryWorker:PollInterval"] = "00:00:00.050",
                });
            });
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<IProviderSender>();
                services.AddSingleton<IProviderSender, InvalidThenSuccessfulProvider>();
            });
        });
        using var client = factory.CreateClient();

        var sentCount = await WaitForSentDeliveryCountAsync(1);

        Assert.Equal(1, sentCount);
    }

    private async Task ResetDatabaseAsync()
    {
        await using var scope = _manualFactory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();

        await dbContext.Database.MigrateAsync(CancellationToken.None);
        await dbContext.Database.ExecuteSqlRawAsync(
            "TRUNCATE otp_challenges, delivery_attempts, deliveries, messages;",
            CancellationToken.None);
    }

    private async Task CreateMessageAsync()
    {
        using var client = await _manualFactory.CreateAuthenticatedMessageClientAsync(
            "Manual Delivery Worker");
        await CreateMessageAsync(client);
    }

    private static async Task CreateMessageAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/messages",
            new CreateMessageRequest(
                Type: "transactional",
                Channel: "sms",
                SenderTitle: "NotifyRail",
                Body: "Your order is ready.",
                Recipients: ["+905551111111"],
                IdempotencyKey: $"delivery-worker-{Guid.NewGuid()}"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private static async Task ResetDatabaseWithoutStartingHostAsync()
    {
        var options = new DbContextOptionsBuilder<NotifyRailDbContext>()
            .UseNpgsql(PostgresConnectionString)
            .Options;

        await using var dbContext = new NotifyRailDbContext(options);
        await dbContext.Database.MigrateAsync(CancellationToken.None);
        await dbContext.Database.ExecuteSqlRawAsync(
            "TRUNCATE otp_challenges, delivery_attempts, deliveries, messages;",
            CancellationToken.None);
    }

    private static async Task<DeliveryState> WaitForDeliveryStatusAsync(string status)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!timeout.IsCancellationRequested)
        {
            var state = await LoadDeliveryStateWithoutStartingHostAsync();
            if (state.Status == status)
            {
                return state;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                break;
            }
        }

        return await LoadDeliveryStateWithoutStartingHostAsync();
    }

    private static async Task<DeliveryState> LoadDeliveryStateWithoutStartingHostAsync()
    {
        var options = new DbContextOptionsBuilder<NotifyRailDbContext>()
            .UseNpgsql(PostgresConnectionString)
            .Options;

        await using var dbContext = new NotifyRailDbContext(options);

        return await dbContext.Database
            .SqlQueryRaw<DeliveryState>(
                DeliveryStateSql)
            .SingleAsync(CancellationToken.None);
    }

    private static async Task<int> WaitForSentDeliveryCountAsync(int expectedCount)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
        while (!timeout.IsCancellationRequested)
        {
            var count = await CountSentDeliveriesAsync();
            if (count == expectedCount)
            {
                return count;
            }

            try
            {
                await Task.Delay(TimeSpan.FromMilliseconds(50), timeout.Token);
            }
            catch (OperationCanceledException) when (timeout.IsCancellationRequested)
            {
                break;
            }
        }

        return await CountSentDeliveriesAsync();
    }

    private static async Task<int> CountSentDeliveriesAsync()
    {
        var options = new DbContextOptionsBuilder<NotifyRailDbContext>()
            .UseNpgsql(PostgresConnectionString)
            .Options;

        await using var dbContext = new NotifyRailDbContext(options);
        return await dbContext.Deliveries.CountAsync(
            delivery => delivery.Status == "sent",
            CancellationToken.None);
    }

    private async Task<DeliveryState> LoadDeliveryStateAsync()
    {
        await using var scope = _manualFactory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();

        return await dbContext.Database
            .SqlQueryRaw<DeliveryState>(
                DeliveryStateSql)
            .SingleAsync(CancellationToken.None);
    }

    private const string DeliveryStateSql =
        """
        SELECT
            deliveries.status AS "Status",
            deliveries.attempt_count AS "AttemptCount",
            deliveries.provider_message_id AS "ProviderMessageId",
            delivery_attempts.provider AS "AttemptProvider",
            delivery_attempts.outcome AS "AttemptOutcome",
            delivery_attempts.provider_message_id AS "AttemptProviderMessageId",
            delivery_attempts.error_code AS "AttemptErrorCode",
            delivery_attempts.error_message AS "AttemptErrorMessage"
        FROM deliveries
        LEFT JOIN delivery_attempts
            ON delivery_attempts.delivery_id = deliveries.id
        """;

    private sealed class DeliveryState
    {
        public string Status { get; init; } = null!;

        public int AttemptCount { get; init; }

        public string? ProviderMessageId { get; init; }

        public string? AttemptProvider { get; init; }

        public string? AttemptOutcome { get; init; }

        public string? AttemptProviderMessageId { get; init; }

        public string? AttemptErrorCode { get; init; }

        public string? AttemptErrorMessage { get; init; }
    }

    private sealed class TransientlyFailingProvider : IProviderSender
    {
        public string Name => "transient-test";

        public Task<ProviderResult> SendAsync(
            ProviderRequest request,
            CancellationToken cancellationToken)
        {
            throw new HttpRequestException("Provider connection failed.");
        }
    }

    private sealed class InvalidThenSuccessfulProvider : IProviderSender
    {
        private int _sendCount;

        public string Name => "invalid-then-successful-test";

        public Task<ProviderResult> SendAsync(
            ProviderRequest request,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var result = Interlocked.Increment(ref _sendCount) == 1
                ? new ProviderResult(ProviderOutcome.Accepted, Provider: " ")
                : new ProviderResult(
                    ProviderOutcome.Accepted,
                    Provider: Name,
                    ProviderMessageId: $"test_{request.IdempotencyKey}");

            return Task.FromResult(result);
        }
    }

    private sealed class TimingOutProvider : IProviderSender
    {
        public string Name => "timeout-test";

        public Task<ProviderResult> SendAsync(
            ProviderRequest request,
            CancellationToken cancellationToken)
        {
            throw new TimeoutException("Provider timed out.");
        }
    }
}
