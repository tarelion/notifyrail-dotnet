using Microsoft.Extensions.Options;
using NotifyRail.Api.Features.Webhooks.Dispatch;
using NotifyRail.Api.Features.Webhooks.Queue;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.Webhooks.Worker;

public sealed class WebhookWorker
{
    private readonly WebhookQueue _queue;
    private readonly WebhookDispatcher _dispatcher;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WebhookWorker> _logger;
    private readonly string _workerId;
    private readonly int _batchSize;

    public WebhookWorker(
        WebhookQueue queue,
        WebhookDispatcher dispatcher,
        IOptions<WebhookWorkerOptions> options,
        TimeProvider timeProvider,
        ILogger<WebhookWorker> logger)
    {
        var workerId = options.Value.WorkerId?.Trim();
        if (string.IsNullOrWhiteSpace(workerId))
        {
            throw new ArgumentException("Worker ID is required.", nameof(options));
        }
        var batchSize = options.Value.BatchSize == 0
            ? WebhookWorkerOptions.DefaultBatchSize
            : options.Value.BatchSize;
        if (batchSize < 1)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Batch size must be greater than zero.");
        }

        _queue = queue;
        _dispatcher = dispatcher;
        _timeProvider = timeProvider;
        _logger = logger;
        _workerId = workerId;
        _batchSize = batchSize;
    }

    public async Task<int> ProcessBatchAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var processed = 0;
        var claimTime = now;
        while (processed < _batchSize)
        {
            var jobs = await _queue.ClaimDueAsync(
                _workerId,
                limit: 1,
                claimTime,
                cancellationToken);
            if (jobs.Count == 0)
            {
                break;
            }

            var job = jobs[0];
            var attemptedAt = PostgresTimestamp.Normalize(_timeProvider.GetUtcNow());
            var result = await SendAsync(job.Request, attemptedAt, cancellationToken);
            var completedAt = PostgresTimestamp.Normalize(_timeProvider.GetUtcNow());
            await _queue.RecordResultAsync(
                job.Claim,
                result,
                attemptedAt,
                completedAt,
                cancellationToken);
            processed++;
            claimTime = completedAt;
        }

        return processed;
    }

    private async Task<WebhookResult> SendAsync(
        WebhookRequest request,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _dispatcher.SendAsync(request, timestamp, cancellationToken);
        }
        catch (Exception exception) when (
            !cancellationToken.IsCancellationRequested
            && exception is HttpRequestException or TimeoutException or TaskCanceledException)
        {
            _logger.LogWarning(exception, "Webhook dispatch failed for event {WebhookEventId}", request.EventId);
            var timedOut = exception is TimeoutException or TaskCanceledException;
            return new WebhookResult(
                Outcome: WebhookOutcome.RetryableFailure,
                HttpStatusCode: null,
                LatencyMilliseconds: 0,
                ErrorCode: timedOut ? "timeout" : "network_error",
                ErrorMessage: timedOut
                    ? "Webhook request timed out."
                    : "Webhook request failed before a response was received.");
        }
    }
}
