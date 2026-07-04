using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotifyRail.Api.Features.Deliveries.Worker;
using NotifyRail.Api.Features.Messages.CreateMessage;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Tests;

public sealed class DeliveryWorkerIntegrationTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DeliveryWorkerIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ProcessBatchAsync_SendsDueDeliveryThroughMockProviderAndRecordsAcceptedResult()
    {
        await ResetDatabaseAsync();
        await CreateMessageAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
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

    private async Task ResetDatabaseAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();

        await dbContext.Database.MigrateAsync(CancellationToken.None);
        await dbContext.Database.ExecuteSqlRawAsync(
            "TRUNCATE delivery_attempts, deliveries, messages;",
            CancellationToken.None);
    }

    private async Task CreateMessageAsync()
    {
        using var client = _factory.CreateClient();
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

    private async Task<DeliveryState> LoadDeliveryStateAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();

        return await dbContext.Database
            .SqlQueryRaw<DeliveryState>(
                """
                SELECT
                    deliveries.status AS "Status",
                    deliveries.attempt_count AS "AttemptCount",
                    deliveries.provider_message_id AS "ProviderMessageId",
                    delivery_attempts.provider AS "AttemptProvider",
                    delivery_attempts.outcome AS "AttemptOutcome",
                    delivery_attempts.provider_message_id AS "AttemptProviderMessageId"
                FROM deliveries
                LEFT JOIN delivery_attempts
                    ON delivery_attempts.delivery_id = deliveries.id
                """)
            .SingleAsync(CancellationToken.None);
    }

    private sealed class DeliveryState
    {
        public string Status { get; init; } = null!;

        public int AttemptCount { get; init; }

        public string? ProviderMessageId { get; init; }

        public string? AttemptProvider { get; init; }

        public string? AttemptOutcome { get; init; }

        public string? AttemptProviderMessageId { get; init; }
    }
}
