using System.Diagnostics;
using Microsoft.Extensions.Options;
using NotifyRail.Api.Features.Webhooks.Dispatch;
using NotifyRail.Api.Features.Webhooks.Queue;
using NotifyRail.Api.Infrastructure.Persistence;
using NotifyRail.Api.Telemetry;

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
        ILogger<WebhookWorker>? logger = null)
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
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<WebhookWorker>.Instance;
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
            using var activity = NotifyRailTelemetry.StartLinkedActivity(
                NotifyRailTelemetry.WebhookDispatchActivity,
                ActivityKind.Consumer,
                job.Request.SourceTraceParent);
            activity?.SetTag(
                NotifyRailTelemetry.ApiClientIdTag,
                job.Request.ApiClientId.ToString());
            activity?.SetTag(
                NotifyRailTelemetry.MessageIdTag,
                job.Request.MessageId.ToString());
            activity?.SetTag(
                NotifyRailTelemetry.DeliveryIdTag,
                job.Request.DeliveryId.ToString());
            activity?.SetTag(
                NotifyRailTelemetry.WebhookEventIdTag,
                job.Request.EventId.ToString());
            activity?.SetTag(
                NotifyRailTelemetry.WebhookAttemptNumberTag,
                job.Claim.AttemptNumber);
            await using var signingLease = await _queue.AcquireSigningLeaseAsync(
                job.Request.ApiClientId,
                cancellationToken);
            var attemptedAt = PostgresTimestamp.Normalize(_timeProvider.GetUtcNow());
            var request = job.Request with
            {
                ProtectedSecret = signingLease.ProtectedSecret,
            };
            var result = await _dispatcher.SendAsync(request, attemptedAt, cancellationToken);
            activity?.SetTag(NotifyRailTelemetry.OutcomeTag, result.Outcome.ToString());
            var completedAt = PostgresTimestamp.Normalize(_timeProvider.GetUtcNow());
            var recorded = await _queue.RecordResultAsync(
                job.Claim,
                result,
                attemptedAt,
                completedAt,
                CancellationToken.None);
            activity?.SetTag(
                NotifyRailTelemetry.WebhookAttemptIdTag,
                recorded.WebhookAttemptId.ToString());
            activity?.SetTag(
                NotifyRailTelemetry.WebhookDispatchStatusTag,
                recorded.EventStatus);
            _logger.LogInformation(
                "Recorded Webhook Attempt {notifyrail.webhook_attempt.id} for Webhook " +
                "Event {notifyrail.webhook_event.id}, Delivery {notifyrail.delivery.id}, " +
                "Message {notifyrail.message.id}, and API Client {notifyrail.api_client.id} " +
                "as {notifyrail.outcome} with event status " +
                "{notifyrail.webhook_event.dispatch_status}",
                recorded.WebhookAttemptId,
                job.Request.EventId,
                job.Request.DeliveryId,
                job.Request.MessageId,
                job.Request.ApiClientId,
                result.Outcome,
                recorded.EventStatus);
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
