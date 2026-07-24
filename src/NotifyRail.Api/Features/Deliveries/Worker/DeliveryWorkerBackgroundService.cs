using Microsoft.Extensions.Options;

namespace NotifyRail.Api.Features.Deliveries.Worker;

public sealed class DeliveryWorkerBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<DeliveryWorkerBackgroundService> _logger;
    private readonly TimeSpan _pollInterval;

    public DeliveryWorkerBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<DeliveryWorkerOptions> options,
        TimeProvider timeProvider,
        ILogger<DeliveryWorkerBackgroundService> logger)
    {
        ArgumentNullException.ThrowIfNull(scopeFactory);
        ArgumentNullException.ThrowIfNull(options);
        ArgumentNullException.ThrowIfNull(timeProvider);
        ArgumentNullException.ThrowIfNull(logger);

        if (options.Value.PollInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(
                nameof(options),
                options.Value.PollInterval,
                "Poll interval must not be negative.");
        }

        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
        _pollInterval = options.Value.PollInterval == TimeSpan.Zero
            ? DeliveryWorkerOptions.DefaultPollInterval
            : options.Value.PollInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await ProcessBatchAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(
                    "Delivery batch failed with {ExceptionType}; polling will continue",
                    exception.GetType().Name);
            }

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
