using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotifyRail.Api.Features.Deliveries.Queue;
using NotifyRail.Api.Features.Messages.CreateMessage;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Tests;

public sealed class DeliveryQueueIntegrationTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public DeliveryQueueIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task ClaimDueAsync_ClaimsDueDeliveryAndReturnsProviderRequest()
    {
        await ResetDatabaseAsync();
        await CreateMessageAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<DeliveryQueue>();

        var jobs = await queue.ClaimDueAsync(
            "worker-a",
            limit: 10,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        var job = Assert.Single(jobs);
        Assert.EndsWith("-attempt-1", job.Request.IdempotencyKey);
        Assert.Equal("+905551111111", job.Request.Recipient);
        Assert.Equal("sms", job.Request.Channel);
        Assert.Equal("NotifyRail", job.Request.SenderTitle);
        Assert.Equal("Your order is ready.", job.Request.Body);
    }

    [Fact]
    public async Task ClaimDueAsync_RejectsBlankWorkerId()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<DeliveryQueue>();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            queue.ClaimDueAsync(
                "   ",
                limit: 10,
                DateTimeOffset.UtcNow,
                CancellationToken.None));

        Assert.Equal("workerId", exception.ParamName);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task ClaimDueAsync_RejectsNonPositiveLimit(int limit)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<DeliveryQueue>();

        var exception = await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            queue.ClaimDueAsync(
                "worker-a",
                limit,
                DateTimeOffset.UtcNow,
                CancellationToken.None));

        Assert.Equal("limit", exception.ParamName);
    }

    [Fact]
    public async Task ClaimDueAsync_RecoversClaimWhenFiveMinuteLeaseExpires()
    {
        await ResetDatabaseAsync();
        await CreateMessageAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<DeliveryQueue>();
        var firstClaimTime = DateTimeOffset.UtcNow;

        var firstJobs = await queue.ClaimDueAsync(
            "worker-a",
            limit: 1,
            firstClaimTime,
            CancellationToken.None);
        var recoveredJobs = await queue.ClaimDueAsync(
            "worker-b",
            limit: 1,
            firstClaimTime.AddMinutes(5),
            CancellationToken.None);

        var firstJob = Assert.Single(firstJobs);
        var recoveredJob = Assert.Single(recoveredJobs);
        Assert.Equal(firstJob.Request.IdempotencyKey, recoveredJob.Request.IdempotencyKey);
    }

    [Fact]
    public async Task ClaimDueAsync_DoesNotReturnExpiredDelivery()
    {
        await ResetDatabaseAsync();
        await CreateMessageAsync();
        var claimTime = DateTimeOffset.UtcNow;
        await SetDeliveryExpiryAsync(claimTime);

        await using var scope = _factory.Services.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<DeliveryQueue>();

        var jobs = await queue.ClaimDueAsync(
            "worker-a",
            limit: 10,
            claimTime,
            CancellationToken.None);

        Assert.Empty(jobs);
    }

    [Fact]
    public async Task ClaimDueAsync_SkipsLockedDeliveryAndClaimsAvailableDelivery()
    {
        await ResetDatabaseAsync();
        await CreateMessageAsync(["+905551111111", "+905552222222"]);

        await using var lockScope = _factory.Services.CreateAsyncScope();
        var lockContext = lockScope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
        await using var lockTransaction =
            await lockContext.Database.BeginTransactionAsync(CancellationToken.None);
        await lockContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            SELECT id
            FROM deliveries
            WHERE recipient = {"+905551111111"}
            FOR UPDATE
            """);

        await using var claimScope = _factory.Services.CreateAsyncScope();
        var queue = claimScope.ServiceProvider.GetRequiredService<DeliveryQueue>();
        var jobs = await queue.ClaimDueAsync(
            "worker-a",
            limit: 10,
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        var job = Assert.Single(jobs);
        Assert.Equal("+905552222222", job.Request.Recipient);
    }

    [Fact]
    public async Task RecordProviderResultAsync_RecordsAcceptedAttemptAndMarksDeliverySent()
    {
        await ResetDatabaseAsync();
        await CreateMessageAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<DeliveryQueue>();
        var job = Assert.Single(await queue.ClaimDueAsync(
            "worker-a",
            limit: 1,
            DateTimeOffset.UtcNow,
            CancellationToken.None));
        var attemptedAt = TruncateToMicroseconds(DateTimeOffset.UtcNow);

        await queue.RecordProviderResultAsync(
            job.Claim,
            new ProviderResult(
                ProviderOutcome.Accepted,
                Provider: "mock",
                ProviderMessageId: "provider-message-1"),
            attemptedAt,
            CancellationToken.None);

        var delivery = await LoadDeliveryAsync();
        var attempt = await LoadDeliveryAttemptAsync();
        Assert.Equal("sent", delivery.Status);
        Assert.Equal(1, delivery.AttemptCount);
        Assert.Null(delivery.NextAttemptAt);
        Assert.Null(delivery.ClaimedAt);
        Assert.Null(delivery.ClaimedBy);
        Assert.Equal("provider-message-1", delivery.ProviderMessageId);
        Assert.Equal(attemptedAt, delivery.UpdatedAt);
        Assert.Equal(1, attempt.AttemptNumber);
        Assert.Equal("mock", attempt.Provider);
        Assert.Equal("accepted", attempt.Outcome);
        Assert.Equal("provider-message-1", attempt.ProviderMessageId);
        Assert.Null(attempt.ErrorCode);
        Assert.Null(attempt.ErrorMessage);
        Assert.Equal(attemptedAt, attempt.AttemptedAt);
    }

    [Fact]
    public async Task RecordProviderResultAsync_RecordsRetryableFailureAndSchedulesRetry()
    {
        await ResetDatabaseAsync();
        await CreateMessageAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<DeliveryQueue>();
        var claimTime = TruncateToMicroseconds(DateTimeOffset.UtcNow);
        var job = Assert.Single(await queue.ClaimDueAsync(
            "worker-a",
            limit: 1,
            claimTime,
            CancellationToken.None));
        var attemptedAt = claimTime.AddSeconds(2);

        await queue.RecordProviderResultAsync(
            job.Claim,
            new ProviderResult(
                ProviderOutcome.RetryableFailure,
                Provider: "mock",
                ErrorCode: "timeout",
                ErrorMessage: "Provider timed out."),
            attemptedAt,
            CancellationToken.None);

        var delivery = await LoadDeliveryAsync();
        var attempt = await LoadDeliveryAttemptAsync();
        Assert.Equal("retry_scheduled", delivery.Status);
        Assert.Equal(1, delivery.AttemptCount);
        Assert.Equal(attemptedAt.AddMinutes(1), delivery.NextAttemptAt);
        Assert.Null(delivery.ProviderMessageId);
        Assert.Null(delivery.ClaimedAt);
        Assert.Null(delivery.ClaimedBy);
        Assert.Equal("retryable_failure", attempt.Outcome);
        Assert.Equal("timeout", attempt.ErrorCode);
        Assert.Equal("Provider timed out.", attempt.ErrorMessage);
        Assert.Null(attempt.ProviderMessageId);
    }

    [Fact]
    public async Task RecordProviderResultAsync_RejectsStaleClaim()
    {
        await ResetDatabaseAsync();
        await CreateMessageAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<DeliveryQueue>();
        var job = Assert.Single(await queue.ClaimDueAsync(
            "worker-a",
            limit: 1,
            DateTimeOffset.UtcNow,
            CancellationToken.None));

        await queue.RecordProviderResultAsync(
            job.Claim,
            new ProviderResult(
                ProviderOutcome.Accepted,
                Provider: "mock",
                ProviderMessageId: "provider-message-1"),
            TruncateToMicroseconds(DateTimeOffset.UtcNow),
            CancellationToken.None);
        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            queue.RecordProviderResultAsync(
                job.Claim,
                new ProviderResult(
                    ProviderOutcome.Accepted,
                    Provider: "mock",
                    ProviderMessageId: "provider-message-2"),
                TruncateToMicroseconds(DateTimeOffset.UtcNow),
                CancellationToken.None));

        var attempts = await LoadDeliveryAttemptsAsync();
        Assert.Equal("Delivery claim is stale.", exception.Message);
        Assert.Single(attempts);
    }

    [Fact]
    public async Task RecordProviderResultAsync_FailsRetryableDeliveryAtMaxAttempts()
    {
        await ResetDatabaseAsync();
        await CreateMessageAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<DeliveryQueue>();
        var firstClaimTime = TruncateToMicroseconds(DateTimeOffset.UtcNow);
        var firstJob = Assert.Single(await queue.ClaimDueAsync(
            "worker-a",
            limit: 1,
            firstClaimTime,
            CancellationToken.None));
        var firstAttemptedAt = firstClaimTime.AddSeconds(1);
        await queue.RecordProviderResultAsync(
            firstJob.Claim,
            new ProviderResult(ProviderOutcome.RetryableFailure, Provider: "mock"),
            firstAttemptedAt,
            CancellationToken.None);

        var secondClaimTime = firstAttemptedAt.AddMinutes(1);
        var secondJob = Assert.Single(await queue.ClaimDueAsync(
            "worker-a",
            limit: 1,
            secondClaimTime,
            CancellationToken.None));
        var secondAttemptedAt = secondClaimTime.AddSeconds(1);
        await queue.RecordProviderResultAsync(
            secondJob.Claim,
            new ProviderResult(ProviderOutcome.RetryableFailure, Provider: "mock"),
            secondAttemptedAt,
            CancellationToken.None);

        var thirdClaimTime = secondAttemptedAt.AddMinutes(2);
        var thirdJob = Assert.Single(await queue.ClaimDueAsync(
            "worker-a",
            limit: 1,
            thirdClaimTime,
            CancellationToken.None));
        var thirdAttemptedAt = thirdClaimTime.AddSeconds(1);
        await queue.RecordProviderResultAsync(
            thirdJob.Claim,
            new ProviderResult(ProviderOutcome.RetryableFailure, Provider: "mock"),
            thirdAttemptedAt,
            CancellationToken.None);

        var delivery = await LoadDeliveryAsync();
        var attempts = await LoadDeliveryAttemptsAsync();
        Assert.Equal("failed", delivery.Status);
        Assert.Equal(3, delivery.AttemptCount);
        Assert.Null(delivery.NextAttemptAt);
        Assert.Equal(3, attempts.Count);
        Assert.All(attempts, attempt => Assert.Equal("retryable_failure", attempt.Outcome));
    }

    [Fact]
    public async Task RecordProviderResultAsync_RecordsPermanentFailureAndMarksDeliveryFailed()
    {
        await ResetDatabaseAsync();
        await CreateMessageAsync();

        await using var scope = _factory.Services.CreateAsyncScope();
        var queue = scope.ServiceProvider.GetRequiredService<DeliveryQueue>();
        var job = Assert.Single(await queue.ClaimDueAsync(
            "worker-a",
            limit: 1,
            DateTimeOffset.UtcNow,
            CancellationToken.None));

        await queue.RecordProviderResultAsync(
            job.Claim,
            new ProviderResult(
                ProviderOutcome.PermanentFailure,
                Provider: "mock",
                ErrorCode: "invalid_recipient"),
            TruncateToMicroseconds(DateTimeOffset.UtcNow),
            CancellationToken.None);

        var delivery = await LoadDeliveryAsync();
        var attempt = await LoadDeliveryAttemptAsync();
        Assert.Equal("failed", delivery.Status);
        Assert.Equal(1, delivery.AttemptCount);
        Assert.Null(delivery.NextAttemptAt);
        Assert.Equal("permanent_failure", attempt.Outcome);
        Assert.Equal("invalid_recipient", attempt.ErrorCode);
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

    private async Task CreateMessageAsync(string[]? recipients = null)
    {
        using var client = _factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/messages",
            new CreateMessageRequest(
                Type: "transactional",
                Channel: "sms",
                SenderTitle: "NotifyRail",
                Body: "Your order is ready.",
                Recipients: recipients ?? ["+905551111111"],
                IdempotencyKey: $"claim-due-{Guid.NewGuid()}"),
            CancellationToken.None);

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
    }

    private async Task SetDeliveryExpiryAsync(DateTimeOffset expiresAt)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE deliveries SET expires_at = {expiresAt}");
    }

    private async Task<DeliveryState> LoadDeliveryAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();

        return await dbContext.Database
            .SqlQueryRaw<DeliveryState>(
                """
                SELECT
                    status AS "Status",
                    attempt_count AS "AttemptCount",
                    next_attempt_at AS "NextAttemptAt",
                    claimed_at AS "ClaimedAt",
                    claimed_by AS "ClaimedBy",
                    provider_message_id AS "ProviderMessageId",
                    updated_at AS "UpdatedAt"
                FROM deliveries
                """)
            .SingleAsync(CancellationToken.None);
    }

    private static DateTimeOffset TruncateToMicroseconds(DateTimeOffset value)
    {
        return value.AddTicks(-(value.Ticks % 10));
    }

    private async Task<DeliveryAttemptState> LoadDeliveryAttemptAsync()
    {
        return Assert.Single(await LoadDeliveryAttemptsAsync());
    }

    private async Task<IReadOnlyList<DeliveryAttemptState>> LoadDeliveryAttemptsAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();

        return await dbContext.Database
            .SqlQueryRaw<DeliveryAttemptState>(
                """
                SELECT
                    attempt_number AS "AttemptNumber",
                    provider AS "Provider",
                    outcome AS "Outcome",
                    provider_message_id AS "ProviderMessageId",
                    error_code AS "ErrorCode",
                    error_message AS "ErrorMessage",
                    attempted_at AS "AttemptedAt"
                FROM delivery_attempts
                ORDER BY attempt_number
                """)
            .ToListAsync(CancellationToken.None);
    }

    private sealed class DeliveryState
    {
        public string Status { get; init; } = null!;

        public int AttemptCount { get; init; }

        public DateTimeOffset? NextAttemptAt { get; init; }

        public DateTimeOffset? ClaimedAt { get; init; }

        public string? ClaimedBy { get; init; }

        public string? ProviderMessageId { get; init; }

        public DateTimeOffset UpdatedAt { get; init; }
    }

    private sealed class DeliveryAttemptState
    {
        public int AttemptNumber { get; init; }

        public string Provider { get; init; } = null!;

        public string Outcome { get; init; } = null!;

        public string? ProviderMessageId { get; init; }

        public string? ErrorCode { get; init; }

        public string? ErrorMessage { get; init; }

        public DateTimeOffset AttemptedAt { get; init; }
    }
}
