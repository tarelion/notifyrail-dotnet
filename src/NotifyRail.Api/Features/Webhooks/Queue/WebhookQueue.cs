using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using NotifyRail.Api.Features.Webhooks.Worker;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.Webhooks.Queue;

public sealed class WebhookQueue
{
    private readonly NotifyRailDbContext _dbContext;
    private readonly IWebhookRetryJitter _jitter;
    private readonly TimeSpan _baseRetryDelay;
    private readonly TimeSpan _minimumRetryDelay;
    private readonly TimeSpan _maximumRetryDelay;
    private readonly double _jitterRatio;
    private readonly TimeSpan _automaticRetryWindow;
    private readonly TimeSpan _claimTimeout;

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
        if (value.AutomaticRetryWindow <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Automatic retry window must be positive.");
        }
        if (value.ClaimTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Claim timeout must be positive.");
        }
        if (value.RequestTimeout <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Request timeout must be positive.");
        }
        if (value.ClaimTimeout <= value.RequestTimeout)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options), "Claim timeout must be greater than the request timeout.");
        }

        _dbContext = dbContext;
        _jitter = jitter;
        _baseRetryDelay = value.BaseRetryDelay;
        _minimumRetryDelay = value.MinimumRetryDelay;
        _maximumRetryDelay = value.MaximumRetryDelay;
        _jitterRatio = value.JitterRatio;
        _automaticRetryWindow = value.AutomaticRetryWindow;
        _claimTimeout = value.ClaimTimeout;
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
        var staleBefore = claimTime.Subtract(_claimTimeout);
        await _dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"""
            UPDATE webhook_events
            SET
                status = 'dead',
                next_attempt_at = NULL,
                claimed_at = NULL,
                claimed_by = NULL,
                updated_at = {claimTime}
            WHERE automatic_attempt_deadline_at <= {claimTime}
                AND (
                    status = 'retry_scheduled'
                    OR (
                        status = 'processing'
                        AND claimed_at <= {staleBefore}
                    )
                )
            """,
            cancellationToken);
        var rows = await _dbContext.Database.SqlQuery<ClaimedWebhookRow>(
            $"""
            WITH due AS (
                SELECT id
                FROM webhook_events AS candidate
                WHERE (
                        (
                            candidate.status IN ('pending', 'retry_scheduled')
                            AND (candidate.next_attempt_at IS NULL OR candidate.next_attempt_at <= {claimTime})
                        )
                        OR (
                            candidate.status = 'processing'
                            AND candidate.claimed_at <= {staleBefore}
                        )
                    )
                    AND NOT EXISTS (
                        SELECT 1
                        FROM webhook_events AS earlier
                        WHERE earlier.delivery_id = candidate.delivery_id
                            AND earlier.sequence < candidate.sequence
                            AND earlier.status NOT IN ('succeeded', 'dead')
                    )
                ORDER BY candidate.created_at, candidate.id
                FOR UPDATE SKIP LOCKED
                LIMIT {limit}
            ), claimed AS (
                UPDATE webhook_events
                SET
                    status = 'processing',
                    next_attempt_at = NULL,
                    automatic_attempt_deadline_at =
                        COALESCE(
                            webhook_events.automatic_attempt_deadline_at,
                            {claimTime} + {_automaticRetryWindow}),
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
                claimed.api_client_id AS "ApiClientId",
                webhook_endpoints.url AS "EndpointUrl",
                claimed.payload AS "Body",
                claimed.attempt_count AS "AttemptCount"
            FROM claimed
            JOIN webhook_endpoints ON webhook_endpoints.id = claimed.webhook_endpoint_id
            ORDER BY claimed.created_at, claimed.id
            """).ToListAsync(cancellationToken);

        var apiClientIds = rows
            .Select(row => row.ApiClientId)
            .Distinct()
            .Order()
            .ToArray();
        if (apiClientIds.Length > 0)
        {
            await _dbContext.Database.SqlQuery<Guid>(
                $"""
                SELECT id AS "Value"
                FROM api_clients
                WHERE id = ANY ({apiClientIds})
                ORDER BY id
                FOR UPDATE
                """).ToListAsync(cancellationToken);
        }

        var protectedSecrets = await _dbContext.WebhookSecrets
            .AsNoTracking()
            .Where(secret =>
                apiClientIds.Contains(secret.ApiClientId)
                && secret.RetiredAt == null)
            .ToDictionaryAsync(
                secret => secret.ApiClientId,
                secret => secret.ProtectedValue,
                cancellationToken);
        if (protectedSecrets.Count != apiClientIds.Length)
        {
            throw new InvalidOperationException(
                "A claimed Webhook Event has no current Webhook Secret.");
        }

        await transaction.CommitAsync(cancellationToken);
        return rows.Select(row => new WebhookJob(
            new WebhookClaim(row.EventId, workerId, row.AttemptCount + 1),
            new WebhookRequest(
                row.EventId,
                row.ApiClientId,
                row.EndpointUrl,
                row.Body,
                protectedSecrets[row.ApiClientId])))
            .ToArray();
    }

    public async Task<WebhookSigningLease> AcquireSigningLeaseAsync(
        Guid apiClientId,
        CancellationToken cancellationToken)
    {
        var connectionString = _dbContext.Database.GetConnectionString();
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new InvalidOperationException(
                "A PostgreSQL connection string is required for Webhook Secret coordination.");
        }

        var connection = new NpgsqlConnection(connectionString);
        try
        {
            await connection.OpenAsync(cancellationToken);
            await using (var lockCommand = connection.CreateCommand())
            {
                lockCommand.CommandText =
                    "SELECT pg_advisory_lock_shared(hashtextextended(@api_client_id, 0))";
                lockCommand.Parameters.AddWithValue(
                    "api_client_id",
                    apiClientId.ToString());
                await lockCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            byte[] protectedSecret;
            await using (var secretCommand = connection.CreateCommand())
            {
                secretCommand.CommandText =
                    """
                    SELECT protected_value
                    FROM webhook_secrets
                    WHERE api_client_id = @api_client_id
                        AND retired_at IS NULL
                    """;
                secretCommand.Parameters.AddWithValue("api_client_id", apiClientId);
                protectedSecret = await secretCommand.ExecuteScalarAsync(cancellationToken)
                    as byte[]
                    ?? throw new InvalidOperationException(
                        "The API Client has no current Webhook Secret.");
            }

            return new WebhookSigningLease(connection, apiClientId, protectedSecret);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
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
        if (result.Outcome is not (
            WebhookOutcome.Succeeded
            or WebhookOutcome.RetryableFailure
            or WebhookOutcome.PermanentFailure))
        {
            throw new ArgumentOutOfRangeException(
                nameof(result), result.Outcome, "Unknown webhook outcome.");
        }
        var retryAt = retryable
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
                status = CASE
                    WHEN {succeeded} THEN 'succeeded'
                    WHEN {retryable}
                        AND {retryAt}::timestamptz < automatic_attempt_deadline_at
                        THEN 'retry_scheduled'
                    ELSE 'dead'
                END,
                attempt_count = {claim.AttemptNumber},
                next_attempt_at = CASE
                    WHEN {retryable}
                        AND {retryAt}::timestamptz < automatic_attempt_deadline_at
                        THEN {retryAt}::timestamptz
                    ELSE NULL
                END,
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
        public Guid ApiClientId { get; init; }
        public string EndpointUrl { get; init; } = null!;
        public string Body { get; init; } = null!;
        public int AttemptCount { get; init; }
    }
}
