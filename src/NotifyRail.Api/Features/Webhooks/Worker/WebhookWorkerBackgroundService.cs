using Microsoft.Extensions.Options;

namespace NotifyRail.Api.Features.Webhooks.Worker;

public sealed class WebhookWorkerBackgroundService
    : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeProvider _timeProvider;
    private readonly ILogger<WebhookWorkerBackgroundService> _logger;
    private readonly TimeSpan _pollInterval;

    public WebhookWorkerBackgroundService(
        IServiceScopeFactory scopeFactory,
        IOptions<WebhookWorkerOptions> options,
        TimeProvider timeProvider,
        ILogger<WebhookWorkerBackgroundService> logger)
    {
        if (options.Value.PollInterval < TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(options), "Poll interval must not be negative.");
        }

        _scopeFactory = scopeFactory;
        _timeProvider = timeProvider;
        _logger = logger;
        _pollInterval = options.Value.PollInterval == TimeSpan.Zero
            ? WebhookWorkerOptions.DefaultPollInterval
            : options.Value.PollInterval;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await using var scope = _scopeFactory.CreateAsyncScope();
                var worker = scope.ServiceProvider.GetRequiredService<WebhookWorker>();
                await worker.ProcessBatchAsync(_timeProvider.GetUtcNow(), stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                _logger.LogError(exception, "Webhook batch failed; polling will continue");
            }

            await Task.Delay(_pollInterval, _timeProvider, stoppingToken);
        }
    }
}
