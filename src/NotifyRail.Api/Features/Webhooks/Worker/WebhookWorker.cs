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
    private readonly string _workerId;
    private readonly int _batchSize;

    public WebhookWorker(
        WebhookQueue queue,
        WebhookDispatcher dispatcher,
        IOptions<WebhookWorkerOptions> options,
        TimeProvider timeProvider)
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
            cancellationToken.ThrowIfCancellationRequested();
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
            var result = await _dispatcher.SendAsync(job.Request, attemptedAt, cancellationToken);
            var completedAt = PostgresTimestamp.Normalize(_timeProvider.GetUtcNow());
            await _queue.RecordResultAsync(
                job.Claim,
                result,
                attemptedAt,
                completedAt,
                CancellationToken.None);
            processed++;
            claimTime = completedAt;
            if (cancellationToken.IsCancellationRequested)
            {
                break;
            }
        }

        return processed;
    }
}
