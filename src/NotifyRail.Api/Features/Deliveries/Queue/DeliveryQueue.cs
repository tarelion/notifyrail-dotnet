using Microsoft.EntityFrameworkCore;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.Deliveries.Queue;

public sealed class DeliveryQueue
{
    private static readonly TimeSpan StaleClaimTimeout = TimeSpan.FromMinutes(5);

    private readonly NotifyRailDbContext _dbContext;

    public DeliveryQueue(NotifyRailDbContext dbContext)
    {
        _dbContext = dbContext;
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

        await using var transaction =
            await _dbContext.Database.BeginTransactionAsync(cancellationToken);

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

    private static DeliveryJob CreateJob(ClaimedDeliveryRow row, string workerId)
    {
        var attemptNumber = row.AttemptCount + 1;

        return new DeliveryJob(
            new DeliveryClaim(row.DeliveryId, workerId, attemptNumber),
            new ProviderRequest(
                $"{row.DeliveryId}-attempt-{attemptNumber}",
                row.Recipient,
                row.Channel,
                row.SenderTitle,
                row.Body));
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
