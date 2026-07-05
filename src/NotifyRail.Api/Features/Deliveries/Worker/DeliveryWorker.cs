using Microsoft.Extensions.Options;
using NotifyRail.Api.Features.Deliveries.Providers;
using NotifyRail.Api.Features.Deliveries.Queue;

namespace NotifyRail.Api.Features.Deliveries.Worker;

public sealed class DeliveryWorker
{
    private readonly DeliveryQueue _queue;
    private readonly IProviderSender _sender;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DeliveryWorker> _logger;
    private readonly string _workerId;
    private readonly int _batchSize;

    public DeliveryWorker(
        DeliveryQueue queue,
        IProviderSender sender,
        IOptions<DeliveryWorkerOptions> options,
        TimeProvider timeProvider,
        ILogger<DeliveryWorker> logger)
    {
        ArgumentNullException.ThrowIfNull(queue);
        ArgumentNullException.ThrowIfNull(sender);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        var workerId = options.Value.WorkerId?.Trim();
        if (string.IsNullOrWhiteSpace(workerId))
        {
            throw new ArgumentException(
                "Worker ID is required.",
                nameof(options));
        }
        var batchSize = options.Value.BatchSize == 0
            ? DeliveryWorkerOptions.DefaultBatchSize
            : options.Value.BatchSize;
        if (batchSize < 1)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.Value.BatchSize,
                "Batch size must be greater than zero.");
        }

        _queue = queue;
        _sender = sender;
        _timeProvider = timeProvider;
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
            var result = await SendAsync(job, cancellationToken);

            await _queue.RecordProviderResultAsync(
                job.Claim,
                result,
                _timeProvider.GetUtcNow(),
                cancellationToken);
            processed++;
        }

        return processed;
    }

    private async Task<ProviderResult> SendAsync(
        DeliveryJob job,
        CancellationToken cancellationToken)
    {
        try
        {
            return await _sender.SendAsync(job.Request, cancellationToken);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TimeoutException)
        {
            _logger.LogWarning(
                exception,
                "Provider {Provider} failed delivery {DeliveryId}; scheduling a retry",
                _sender.Name,
                job.Claim.DeliveryId);
            return new ProviderResult(
                ProviderOutcome.RetryableFailure,
                _sender.Name,
                ErrorCode: "provider_exception",
                ErrorMessage: exception.Message);
        }
    }
}
