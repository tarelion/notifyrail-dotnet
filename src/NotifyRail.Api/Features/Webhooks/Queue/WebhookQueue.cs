using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NotifyRail.Api.Features.Webhooks.Worker;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.Webhooks.Queue;

public sealed class WebhookQueue
{
    private static readonly TimeSpan StaleClaimTimeout = TimeSpan.FromMinutes(5);
    private readonly NotifyRailDbContext _dbContext;
    private readonly IWebhookRetryJitter _jitter;
    private readonly TimeSpan _baseRetryDelay;
    private readonly TimeSpan _minimumRetryDelay;
    private readonly TimeSpan _maximumRetryDelay;
    private readonly double _jitterRatio;

    public WebhookQueue(
        NotifyRailDbContext dbContext,
        IOptions<WebhookWorkerOptions> options,
        IWebhookRetryJitter jitter)
    {
        var value = options.Value;
        if (value.MinimumRetryDelay <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Minimum retry delay must be positive.");
        }
        if (value.BaseRetryDelay < value.MinimumRetryDelay)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), "Base retry delay must not be less than the minimum retry delay.");
        }
        if (value.MaximumRetryDelay < value.BaseRetryDelay)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), "Maximum retry delay must not be less than the base retry delay.");
        }
        if (value.JitterRatio is < 0 or > 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Jitter ratio must be between zero and one.");
        }

        _dbContext = dbContext;
        _jitter = jitter;
        _baseRetryDelay = value.BaseRetryDelay;
        _minimumRetryDelay = value.MinimumRetryDelay;
        _maximumRetryDelay = value.MaximumRetryDelay;
        _jitterRatio = value.JitterRatio;
    }

    public async Task<IReadOnlyList<WebhookJob>> ClaimDueAsync(
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
            throw new ArgumentOutOfRangeException(nameof(limit), limit, "Limit must be greater than zero.");
        }

        workerId = workerId.Trim();
        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var staleBefore = claimTime.Subtract(StaleClaimTimeout);
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE webhook_events
            SET status = 'pending', claimed_at = NULL, claimed_by = NULL, updated_at = {claimTime}
            WHERE status = 'processing' AND claimed_at <= {staleBefore}
            """,
            cancellationToken);

        var rows = await _dbContext.Database.SqlQuery<ClaimedWebhookRow>(
            $"""
            WITH due AS (
                SELECT id
                FROM webhook_events AS candidate
                WHERE candidate.status IN ('pending', 'retry_scheduled')
                    AND (candidate.next_attempt_at IS NULL OR candidate.next_attempt_at <= {claimTime})
                    AND NOT EXISTS (
                        SELECT 1
                        FROM webhook_events AS earlier
                        WHERE earlier.delivery_id = candidate.delivery_id
                            AND earlier.sequence < candidate.sequence
                            AND earlier.status NOT IN ('succeeded', 'failed')
                    )
                ORDER BY candidate.created_at, candidate.id
                FOR UPDATE SKIP LOCKED
                LIMIT {limit}
            ), claimed AS (
                UPDATE webhook_events
                SET
                    status = 'processing',
                    next_attempt_at = NULL,
                    claimed_at = {claimTime},
                    claimed_by = {workerId},
                    updated_at = {claimTime}
                FROM due
                WHERE webhook_events.id = due.id
                RETURNING
                    webhook_events.id,
                    webhook_events.webhook_endpoint_id,
                    webhook_events.api_client_id,
                    webhook_events.payload,
                    webhook_events.attempt_count,
                    webhook_events.created_at
            )
            SELECT
                claimed.id AS "EventId",
                webhook_endpoints.url AS "EndpointUrl",
                claimed.payload AS "Body",
                webhook_secrets.protected_value AS "ProtectedSecret",
                claimed.attempt_count AS "AttemptCount"
            FROM claimed
            JOIN webhook_endpoints ON webhook_endpoints.id = claimed.webhook_endpoint_id
            JOIN webhook_secrets ON webhook_secrets.api_client_id = claimed.api_client_id
            ORDER BY claimed.created_at, claimed.id
            """).ToListAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);
        return rows.Select(row => new WebhookJob(
            new WebhookClaim(row.EventId, workerId, row.AttemptCount + 1),
            new WebhookRequest(row.EventId, row.EndpointUrl, row.Body, row.ProtectedSecret)))
            .ToArray();
    }

    public async Task RecordResultAsync(
        WebhookClaim claim,
        WebhookResult result,
        DateTimeOffset attemptedAt,
        DateTimeOffset completedAt,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(claim);
        ArgumentNullException.ThrowIfNull(result);
        var errorCode = Bound(result.ErrorCode, 100);
        var errorMessage = Bound(result.ErrorMessage, 500);
        var outcome = ToDatabaseValue(result.Outcome);
        var succeeded = result.Outcome == WebhookOutcome.Succeeded;
        var retryable = result.Outcome == WebhookOutcome.RetryableFailure;
        var eventStatus = result.Outcome switch
        {
            WebhookOutcome.Succeeded => "succeeded",
            WebhookOutcome.RetryableFailure => "retry_scheduled",
            WebhookOutcome.PermanentFailure => "failed",
            _ => throw new ArgumentOutOfRangeException(
                nameof(result), result.Outcome, "Unknown webhook outcome."),
        };
        var nextAttemptAt = retryable
            ? completedAt.Add(RetryDelay(claim.AttemptNumber, result.RetryAfter, completedAt))
            : (DateTimeOffset?)null;

        await using var transaction = await _dbContext.Database.BeginTransactionAsync(cancellationToken);
        var recorded = await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            INSERT INTO webhook_attempts (
                webhook_event_id, attempt_number, outcome, http_status_code,
                error_code, error_message, attempted_at, completed_at, latency_milliseconds)
            SELECT
                candidate.id, {claim.AttemptNumber}, {outcome}, {result.HttpStatusCode},
                {errorCode}, {errorMessage}, {attemptedAt}, {completedAt}, {result.LatencyMilliseconds}
            FROM (
                SELECT id
                FROM webhook_events
                WHERE id = {claim.EventId}
                    AND status = 'processing'
                    AND claimed_by = {claim.WorkerId}
                    AND attempt_count = {claim.AttemptNumber} - 1
                FOR UPDATE
            ) AS candidate
            """,
            cancellationToken);
        if (recorded == 0)
        {
            throw new InvalidOperationException("Webhook claim is stale.");
        }

        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE webhook_events
            SET
                status = {eventStatus},
                attempt_count = {claim.AttemptNumber},
                next_attempt_at = {nextAttemptAt},
                claimed_at = NULL,
                claimed_by = NULL,
                succeeded_at = CASE WHEN {succeeded} THEN {completedAt}::timestamptz ELSE NULL END,
                updated_at = {completedAt}
            WHERE id = {claim.EventId}
            """,
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    private static string? Bound(string? value, int maximumLength)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var normalized = value.Trim();
        return normalized.Length <= maximumLength ? normalized : normalized[..maximumLength];
    }

    private TimeSpan RetryDelay(
        int attemptNumber,
        WebhookRetryAfter? retryAfter,
        DateTimeOffset completedAt)
    {
        var requestedDelay = retryAfter switch
        {
            WebhookRetryAfter.Relative relative => relative.Delay,
            WebhookRetryAfter.Absolute absolute => absolute.RetryAt - completedAt,
            null => (TimeSpan?)null,
            _ => throw new ArgumentOutOfRangeException(
                nameof(retryAfter), retryAfter, "Unknown Retry-After value."),
        };
        if (requestedDelay is { } delay)
        {
            return TimeSpan.FromTicks(Math.Clamp(
                delay.Ticks,
                _minimumRetryDelay.Ticks,
                _maximumRetryDelay.Ticks));
        }

        var jitterValue = _jitter.NextUnitIntervalSample();
        if (jitterValue is < 0 or > 1)
        {
            throw new InvalidOperationException("Webhook retry jitter must be between zero and one.");
        }

        var exponent = Math.Pow(2, Math.Min(attemptNumber - 1, 62));
        var exponentialTicks = Math.Min(
            _maximumRetryDelay.Ticks,
            _baseRetryDelay.Ticks * exponent);
        var multiplier = 1 + (((jitterValue * 2) - 1) * _jitterRatio);
        var jitteredTicks = (long)Math.Round(exponentialTicks * multiplier);
        return TimeSpan.FromTicks(Math.Clamp(
            jitteredTicks,
            _minimumRetryDelay.Ticks,
            _maximumRetryDelay.Ticks));
    }

    private static string ToDatabaseValue(WebhookOutcome outcome)
    {
        return outcome switch
        {
            WebhookOutcome.Succeeded => "succeeded",
            WebhookOutcome.RetryableFailure => "retryable_failure",
            WebhookOutcome.PermanentFailure => "permanent_failure",
            _ => throw new ArgumentOutOfRangeException(nameof(outcome), outcome, "Unknown webhook outcome."),
        };
    }

    private sealed class ClaimedWebhookRow
    {
        public Guid EventId { get; init; }
        public string EndpointUrl { get; init; } = null!;
        public string Body { get; init; } = null!;
        public byte[] ProtectedSecret { get; init; } = null!;
        public int AttemptCount { get; init; }
    }
}
