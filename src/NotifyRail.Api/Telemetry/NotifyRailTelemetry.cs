using System.Diagnostics;

namespace NotifyRail.Api.Telemetry;

public static class NotifyRailTelemetry
{
    public const string ActivitySourceName = "NotifyRail.Api";

    public const string MessageIntakeActivity = "notifyrail.message.intake";
    public const string DeliveryProcessActivity = "notifyrail.delivery.process";
    public const string ProviderCallbackActivity = "notifyrail.provider_callback.handle";
    public const string WebhookEventCreateActivity = "notifyrail.webhook_event.create";
    public const string WebhookDispatchActivity = "notifyrail.webhook.dispatch";
    public const string WebhookReplayActivity = "notifyrail.webhook.replay";

    public const string ApiClientIdTag = "notifyrail.api_client.id";
    public const string MessageIdTag = "notifyrail.message.id";
    public const string DeliveryIdTag = "notifyrail.delivery.id";
    public const string WebhookEventIdTag = "notifyrail.webhook_event.id";
    public const string WebhookAttemptIdTag = "notifyrail.webhook_attempt.id";
    public const string DeliveryCountTag = "notifyrail.delivery.count";
    public const string RecipientTag = "notifyrail.recipient.masked";
    public const string DeliveryAttemptNumberTag = "notifyrail.delivery_attempt.number";
    public const string OutcomeTag = "notifyrail.outcome";
    public const string WebhookEventTypeTag = "notifyrail.webhook_event.type";
    public const string WebhookAttemptNumberTag = "notifyrail.webhook_attempt.number";
    public const string WebhookDispatchStatusTag = "notifyrail.webhook_event.dispatch_status";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);

    internal static string? CaptureCurrentTraceParent()
    {
        return Activity.Current is { IdFormat: ActivityIdFormat.W3C } activity
            ? activity.Id
            : null;
    }

    internal static Activity? StartLinkedActivity(
        string name,
        ActivityKind kind,
        string? traceParent)
    {
        var links = ActivityContext.TryParse(
            traceParent,
            traceState: null,
            isRemote: false,
            out var context)
            ? new[] { new ActivityLink(context) }
            : [];

        return ActivitySource.StartActivity(
            name,
            kind,
            parentContext: default,
            tags: null,
            links);
    }

    internal static string MaskRecipient(string recipient)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(recipient);

        var normalized = recipient.Trim();
        if (normalized.Length <= 4)
        {
            return "***";
        }

        return string.Concat(
            normalized.AsSpan(0, 2),
            new string('*', normalized.Length - 4),
            normalized.AsSpan(normalized.Length - 2));
    }
}
