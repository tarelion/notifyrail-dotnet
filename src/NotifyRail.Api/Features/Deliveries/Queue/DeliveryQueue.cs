using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NotifyRail.Api.Features.Webhooks.Outbox;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.Deliveries.Queue;

public sealed class DeliveryQueue
{
    private const int MaxAttempts = 3;

    private static readonly TimeSpan StaleClaimTimeout = TimeSpan.FromMinutes(5);

    private readonly NotifyRailDbContext _dbContext;

    private readonly DeliveryWebhookOutbox _webhookOutbox;

    private readonly TimeSpan _baseRetryDelay;

    public DeliveryQueue(
        NotifyRailDbContext dbContext,
        DeliveryWebhookOutbox webhookOutbox,
        IOptions<DeliveryQueueOptions> options)
    {
        _dbContext = dbContext;
        _webhookOutbox = webhookOutbox;
        _baseRetryDelay = options.Value.BaseRetryDelay;
    }

    public async Task<IReadOnlyList<DeliveryJob>> ClaimDueAsync(
        string workerId,
        int limit,
        DateTimeOffset claimTime,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(workerId))
        {
            throw new ArgumentException("Worker ID is required.", nameof(workerId));
        }
        if (limit < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(limit),
                limit,
                "Limit must be greater than zero.");
        }

        workerId = workerId.Trim();

        // Recovery, expiry, and claiming share one transaction so workers cannot
        // observe or act on partially transitioned deliveries.
        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // A worker can stop after claiming a delivery but before recording the
        // provider result. Reusing the current attempt number lets a replacement
        // worker safely repeat that attempt with the same provider idempotency key.
        var staleBefore = claimTime.Subtract(StaleClaimTimeout);
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE deliveries
            SET
                status = CASE
                    WHEN attempt_count = 0 THEN 'queued'
                    ELSE 'retry_scheduled'
                END,
                next_attempt_at = CASE
                    WHEN attempt_count = 0 THEN NULL
                    ELSE {claimTime}
                END,
                claimed_at = NULL,
                claimed_by = NULL,
                updated_at = {claimTime}
            WHERE status = 'processing'
                AND claimed_at <= {staleBefore}
            """,
            cancellationToken);

        // PostgreSQL data-modifying CTEs share a snapshot and cannot see one
        // another's table changes. The expired CTE's RETURNING output is therefore
        // used explicitly to keep newly expired rows out of the due set.
        var rows = await _dbContext.Database
            .SqlQuery<ClaimedDeliveryRow>(
                $"""
                WITH expired AS (
                    UPDATE deliveries
                    SET
                        status = 'expired',
                        next_attempt_at = NULL,
                        claimed_at = NULL,
                        claimed_by = NULL,
                        updated_at = {claimTime}
                    WHERE status IN ('queued', 'retry_scheduled', 'sent')
                        AND expires_at <= {claimTime}
                    RETURNING id
                ), due AS (
                    SELECT deliveries.id
                    FROM deliveries
                    JOIN messages ON messages.id = deliveries.message_id
                    WHERE (
                        (
                            deliveries.status = 'queued'
                            AND (
                                messages.scheduled_at IS NULL
                                OR messages.scheduled_at <= {claimTime}
                            )
                        )
                        OR (
                            deliveries.status = 'retry_scheduled'
                            AND deliveries.next_attempt_at <= {claimTime}
                        )
                    )
                    AND (
                        deliveries.expires_at IS NULL
                        OR deliveries.expires_at > {claimTime}
                    )
                    AND NOT EXISTS (
                        SELECT 1
                        FROM expired
                        WHERE expired.id = deliveries.id
                    )
                    ORDER BY
                        CASE messages.type
                            WHEN 'otp' THEN 100
                            WHEN 'transactional' THEN 50
                            WHEN 'campaign' THEN 10
                        END DESC,
                        deliveries.created_at,
                        deliveries.id
                    -- SKIP LOCKED lets concurrent workers make progress without
                    -- waiting; ordering is deterministic only among unlocked rows.
                    FOR UPDATE OF deliveries SKIP LOCKED
                    LIMIT {limit}
                ), claimed AS (
                    UPDATE deliveries
                    SET
                        status = 'processing',
                        next_attempt_at = NULL,
                        claimed_at = {claimTime},
                        claimed_by = {workerId},
                        updated_at = {claimTime}
                    FROM due
                    WHERE deliveries.id = due.id
                    RETURNING
                        deliveries.id,
                        deliveries.message_id,
                        deliveries.recipient,
                        deliveries.attempt_count,
                        deliveries.created_at
                )
                SELECT
                    claimed.id AS "DeliveryId",
                    claimed.recipient AS "Recipient",
                    messages.channel AS "Channel",
                    messages.sender_title AS "SenderTitle",
                    messages.body AS "Body",
                    claimed.attempt_count AS "AttemptCount"
                FROM claimed
                JOIN messages ON messages.id = claimed.message_id
                ORDER BY
                    CASE messages.type
                        WHEN 'otp' THEN 100
                        WHEN 'transactional' THEN 50
                        WHEN 'campaign' THEN 10
                    END DESC,
                    claimed.created_at,
                    claimed.id
                """)
            .ToListAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return rows
            .Select(row => CreateJob(row, workerId))
            .ToArray();
    }

    public async Task RecordProviderResultAsync(
        DeliveryClaim claim,
        ProviderResult result,
        DateTimeOffset attemptedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(result);

        if (claim.DeliveryId == Guid.Empty)
        {
            throw new ArgumentException("Delivery ID is required.", nameof(claim));
        }
        if (string.IsNullOrWhiteSpace(claim.WorkerId))
        {
            throw new ArgumentException("Worker ID is required.", nameof(claim));
        }
        if (claim.AttemptNumber < 1)
        {
            throw new ArgumentException("Attempt number must be greater than zero.", nameof(claim));
        }
        if (string.IsNullOrWhiteSpace(result.Provider))
        {
            throw new ArgumentException("Provider is required.", nameof(result));
        }
        if (!Enum.IsDefined(result.Outcome))
        {
            throw new ArgumentOutOfRangeException(nameof(result), result.Outcome, "Unknown provider outcome.");
        }
        if (attemptedAt == default)
        {
            throw new ArgumentException("Attempted time is required.", nameof(attemptedAt));
        }

        var workerId = claim.WorkerId.Trim();
        var provider = result.Provider.Trim();
        var outcome = ToDatabaseValue(result.Outcome);
        var providerMessageId = NormalizeOptional(result.ProviderMessageId);
        var errorCode = NormalizeOptional(result.ErrorCode);
        var errorMessage = NormalizeOptional(result.ErrorMessage);
        var nextAttemptAt = ShouldScheduleRetry(result.Outcome, claim.AttemptNumber)
            ? attemptedAt.Add(RetryDelay(claim.AttemptNumber))
            : (DateTimeOffset?)null;

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);

        // The conditional insert both validates and locks the current claim. A
        // zero-row insert means another worker or lease recovery made it stale;
        // the surrounding transaction keeps the attempt and state change atomic.
        var recorded = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO delivery_attempts (
                delivery_id,
                attempt_number,
                provider,
                outcome,
                provider_message_id,
                error_code,
                error_message,
                attempted_at
            )
            SELECT
                candidate.id,
                {claim.AttemptNumber},
                {provider},
                {outcome},
                {providerMessageId},
                {errorCode},
                {errorMessage},
                {attemptedAt}
            FROM (
                SELECT deliveries.id
                FROM deliveries
                WHERE deliveries.id = {claim.DeliveryId}
                    AND deliveries.status = 'processing'
                    AND deliveries.claimed_by = {workerId}
                    AND deliveries.attempt_count = {claim.AttemptNumber} - 1
                FOR UPDATE
            ) AS candidate
            """,
            cancellationToken);

        if (recorded == 0)
        {
            throw new InvalidOperationException("Delivery claim is stale.");
        }

        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE deliveries
            SET
                status = CASE
                    WHEN {outcome} = 'accepted' THEN 'sent'
                    WHEN {outcome} = 'retryable_failure'
                        AND {claim.AttemptNumber} < {MaxAttempts}
                        THEN 'retry_scheduled'
                    ELSE 'failed'
                END,
                attempt_count = {claim.AttemptNumber},
                next_attempt_at = CASE
                    WHEN {outcome} = 'retryable_failure'
                        AND {claim.AttemptNumber} < {MaxAttempts}
                        THEN {nextAttemptAt}::timestamptz
                    ELSE NULL
                END,
                provider_message_id = CASE
                    WHEN {outcome} = 'accepted' THEN {providerMessageId}
                    ELSE NULL
                END,
                claimed_at = NULL,
                claimed_by = NULL,
                updated_at = {attemptedAt}
            WHERE id = {claim.DeliveryId}
            """,
            cancellationToken);

        if (result.Outcome == ProviderOutcome.Accepted)
        {
            await _webhookOutbox.CreateDeliverySentAsync(
                claim.DeliveryId,
                attemptedAt,
                cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
    }

    private static DeliveryJob CreateJob(ClaimedDeliveryRow row, string workerId)
    {
        var attemptNumber = row.AttemptCount + 1;

        // A recovered send repeats the same attempt key, while a scheduled retry
        // gets a new key. This preserves provider idempotency across worker failure.
        return new DeliveryJob(
            new DeliveryClaim(row.DeliveryId, workerId, attemptNumber),
            new ProviderRequest(
                $"{row.DeliveryId}-attempt-{attemptNumber}",
                row.Recipient,
                row.Channel,
                row.SenderTitle,
                row.Body,
                attemptNumber));
    }

    private static string ToDatabaseValue(ProviderOutcome outcome)
    {
        return outcome switch
        {
            ProviderOutcome.Accepted => "accepted",
            ProviderOutcome.RetryableFailure => "retryable_failure",
            ProviderOutcome.PermanentFailure => "permanent_failure",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown provider outcome."),
        };
    }

    private static string? NormalizeOptional(string? value)
    {
        return string.IsNullOrWhiteSpace(value) ? null : value.Trim();
    }

    private static bool ShouldScheduleRetry(ProviderOutcome outcome, int attemptNumber)
    {
        return outcome == ProviderOutcome.RetryableFailure && attemptNumber < MaxAttempts;
    }

    private TimeSpan RetryDelay(int attemptNumber)
    {
        return attemptNumber * _baseRetryDelay;
    }

    private sealed class ClaimedDeliveryRow
    {
        public Guid DeliveryId { get; init; }

        public string Recipient { get; init; } = null!;

        public string Channel { get; init; } = null!;

        public string SenderTitle { get; init; } = null!;

        public string Body { get; init; } = null!;

        public int AttemptCount { get; init; }
    }
}
