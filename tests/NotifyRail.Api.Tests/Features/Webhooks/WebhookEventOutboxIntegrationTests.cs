using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NotifyRail.Api.Features.ApiClients.CreateApiClient;
using NotifyRail.Api.Features.Deliveries.Queue;
using NotifyRail.Api.Features.Messages.CreateMessage;
using NotifyRail.Api.Features.Webhooks.RegisterWebhookEndpoint;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Tests;

public sealed class WebhookEventOutboxIntegrationTests
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public WebhookEventOutboxIntegrationTests(WebApplicationFactory<Program> factory)
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
    public async Task RecordProviderResultAsync_CreatesVersionedDeliverySentEventForActiveEndpoint()
    {
        await ResetDatabaseAsync();
        var apiClientId = await CreateApiClientWithEndpointAsync();
        var messageId = await CreateMessageAsync(apiClientId);

        await using var scope = _factory.Services.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<DeliveryQueue>();
        var job = Assert.Single(await queue.ClaimDueAsync(
            "delivery-worker",
            limit: 1,
            DateTimeOffset.UtcNow,
            CancellationToken.None));
        var deliveryId = await LoadClaimedDeliveryIdAsync();
        var occurredAt = TruncateToMicroseconds(DateTimeOffset.UtcNow);

        await queue.RecordProviderResultAsync(
            job.Claim,
            new ProviderResult(
                ProviderOutcome.Accepted,
                Provider: "mock",
                ProviderMessageId: "provider-message-1"),
            occurredAt,
            CancellationToken.None);

        var webhookEvent = await LoadWebhookEventAsync();
        Assert.NotEqual(Guid.Empty, webhookEvent.Id);
        Assert.Equal("delivery.sent", webhookEvent.Type);
        Assert.Equal(1, webhookEvent.Version);
        Assert.Equal(occurredAt, webhookEvent.OccurredAt);
        Assert.Equal(messageId, webhookEvent.MessageId);
        Assert.Equal(deliveryId, webhookEvent.DeliveryId);
        Assert.Equal(1, webhookEvent.Sequence);
        Assert.Equal("pending", webhookEvent.Status);

        using var payload = JsonDocument.Parse(webhookEvent.Payload);
        var root = payload.RootElement;
        Assert.Equal(webhookEvent.Id, root.GetProperty("event_id").GetGuid());
        Assert.Equal("delivery.sent", root.GetProperty("type").GetString());
        Assert.Equal(1, root.GetProperty("version").GetInt32());
        Assert.Equal(occurredAt, root.GetProperty("occurred_at").GetDateTimeOffset());
        var data = root.GetProperty("data");
        Assert.Equal(messageId, data.GetProperty("message_id").GetGuid());
        Assert.Equal(deliveryId, data.GetProperty("delivery_id").GetGuid());
        Assert.Equal(1, data.GetProperty("sequence").GetInt32());
        Assert.Equal("sent", data.GetProperty("status").GetString());
        Assert.Equal("+905551111111", data.GetProperty("recipient").GetString());
        Assert.DoesNotContain("Your order is ready.", webhookEvent.Payload, StringComparison.Ordinal);
        Assert.DoesNotContain("provider-message-1", webhookEvent.Payload, StringComparison.Ordinal);
    }

    [Fact]
    public async Task RecordProviderResultAsync_DoesNotCreateEventWithoutActiveEndpoint()
    {
        await ResetDatabaseAsync();
        var apiClientId = await CreateApiClientAsync();
        await CreateMessageAsync(apiClientId);

        await RecordAcceptedProviderResultAsync("provider-message-1");

        Assert.Equal(0, await CountWebhookEventsAsync());
    }

    [Fact]
    public async Task RecordProviderResultAsync_RollsBackEventWhenDeliveryTransitionFails()
    {
        await ResetDatabaseAsync();
        var apiClientId = await CreateApiClientWithEndpointAsync();
        await CreateMessageAsync(apiClientId, ["+905551111111", "+905552222222"]);
        await RecordAcceptedProviderResultAsync("duplicate-provider-id");

        await using var scope = _factory.Services.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<DeliveryQueue>();
        var secondJob = Assert.Single(await queue.ClaimDueAsync(
            "delivery-worker",
            limit: 1,
            DateTimeOffset.UtcNow,
            CancellationToken.None));

        await Assert.ThrowsAsync<PostgresException>(() => queue.RecordProviderResultAsync(
            secondJob.Claim,
            new ProviderResult(
                ProviderOutcome.Accepted,
                Provider: "mock",
                ProviderMessageId: "duplicate-provider-id"),
            TruncateToMicroseconds(DateTimeOffset.UtcNow),
            CancellationToken.None));

        Assert.Equal(1, await CountWebhookEventsAsync());
    }

    private async Task<Guid> CreateApiClientAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var creator = scope.ServiceProvider.GetRequiredService<ApiClientCreator>();
        var created = await creator.CreateAsync("Webhook outbox client", CancellationToken.None);
        return created.ApiClientId;
    }

    private async Task<Guid> CreateApiClientWithEndpointAsync()
    {
        var apiClientId = await CreateApiClientAsync();
        await using var scope = _factory.Services.CreateAsyncScope();
        var registrar = scope.ServiceProvider.GetRequiredService<WebhookEndpointRegistrar>();
        var registered = await registrar.RegisterAsync(
            apiClientId,
            "https://hooks.example.com/notifyrail",
            CancellationToken.None);
        Assert.NotNull(registered);
        return apiClientId;
    }

    private async Task<Guid> CreateMessageAsync(
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
                IdempotencyKey: $"webhook-outbox-{Guid.NewGuid()}",
                ScheduledAt: null,
                ReportLabel: null,
                Encoding: null),
            CancellationToken.None);
        return outcome.Response!.MessageId;
    }

    private async Task RecordAcceptedProviderResultAsync(string providerMessageId)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<DeliveryQueue>();
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
                ProviderMessageId: providerMessageId),
            TruncateToMicroseconds(DateTimeOffset.UtcNow),
            CancellationToken.None);
    }

    private async Task ResetDatabaseAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
        await dbContext.Database.MigrateAsync(CancellationToken.None);
        await dbContext.Database.ExecuteSqlRawAsync(
            "TRUNCATE webhook_secrets, webhook_endpoints, otp_challenges, " +
            "delivery_attempts, deliveries, messages, api_keys, api_clients CASCADE;",
            CancellationToken.None);
    }

    private async Task<WebhookEventState> LoadWebhookEventAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
        return await dbContext.Database.SqlQueryRaw<WebhookEventState>(
            """
            SELECT
                id AS "Id",
                type AS "Type",
                version AS "Version",
                occurred_at AS "OccurredAt",
                message_id AS "MessageId",
                delivery_id AS "DeliveryId",
                sequence AS "Sequence",
                status AS "Status",
                payload AS "Payload"
            FROM webhook_events
            """).SingleAsync(CancellationToken.None);
    }

    private async Task<int> CountWebhookEventsAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
        return await dbContext.Database.SqlQueryRaw<int>(
            "SELECT count(*)::int AS \"Value\" FROM webhook_events")
            .SingleAsync(CancellationToken.None);
    }

    private async Task<Guid> LoadClaimedDeliveryIdAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
        return await dbContext.Database.SqlQueryRaw<Guid>(
            "SELECT id AS \"Value\" FROM deliveries WHERE status = 'processing'")
            .SingleAsync(CancellationToken.None);
    }

    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value)
    {
        return value.AddTicks(-(value.Ticks % 10));
    }

    private sealed class WebhookEventState
    {
        public Guid Id { get; init; }
        public string Type { get; init; } = null!;
        public int Version { get; init; }
        public DateTimeOffset OccurredAt { get; init; }
        public Guid MessageId { get; init; }
        public Guid DeliveryId { get; init; }
        public int Sequence { get; init; }
        public string Status { get; init; } = null!;
        public string Payload { get; init; } = null!;
    }
}
