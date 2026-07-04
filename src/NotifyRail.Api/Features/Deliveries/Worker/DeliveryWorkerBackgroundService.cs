using Microsoft.Extensions.Options;

namespace NotifyRail.Api.Features.Deliveries.Worker;

public sealed class DeliveryWorkerBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly TimeSpan _pollInterval;

    public DeliveryWorkerBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<DeliveryWorkerOptions> options,
        TimeProvider timeProvider)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);

        if (options.Value.PollInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.Value.PollInterval,
                "Poll interval must not be negative.");
        }

        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _pollInterval = options.Value.PollInterval == TimeSpan.Zero
            ? DeliveryWorkerOptions.DefaultPollInterval
            : options.Value.PollInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await ProcessBatchAsync(stoppingToken);
            await Task.Delay(_pollInterval, _timeProvider, stoppingToken);
        }
    }

    private async Task ProcessBatchAsync(CancellationToken stoppingToken)
    {
        await using var scope = _scopeFactory.CreateAsyncScope();
        var worker = scope.ServiceProvider.GetRequiredService<DeliveryWorker>();

        await worker.ProcessBatchAsync(
            _timeProvider.GetUtcNow(),
            stoppingToken);
    }
}
