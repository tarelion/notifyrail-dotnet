using Microsoft.Extensions.Options;
using NotifyRail.Api.Features.Webhooks.Dispatch;
using NotifyRail.Api.Features.Webhooks.Queue;

namespace NotifyRail.Api.Features.Webhooks.Worker;

public sealed class WebhookWorker
{
    private readonly WebhookQueue _queue;
    private readonly WebhookDispatcher _dispatcher;
    private readonly ILogger<WebhookWorker> _logger;
    private readonly string _workerId;
    private readonly int _batchSize;

    public WebhookWorker(
        WebhookQueue queue,
        WebhookDispatcher dispatcher,
        IOptions<WebhookWorkerOptions> options,
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
        _logger = logger;
        _workerId = workerId;
        _batchSize = batchSize;
    }

    public async Task<int> ProcessBatchAsync(
        DateTimeOffset now,
        CancellationToken cancellationToken)
    {
        var jobs = await _queue.ClaimDueAsync(
            _workerId,
            _batchSize,
            now,
            cancellationToken);
        var processed = 0;
        foreach (var job in jobs)
        {
            var result = await SendAsync(job.Request, now, cancellationToken);
            await _queue.RecordResultAsync(job.Claim, result, now, cancellationToken);
            processed++;
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
            return new WebhookResult(
                Succeeded: false,
                HttpStatusCode: null,
                LatencyMilliseconds: 0,
                ErrorCode: "network_error",
                ErrorMessage: exception.Message);
        }
    }
}
