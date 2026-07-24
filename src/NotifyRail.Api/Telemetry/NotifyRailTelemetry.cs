using System.Diagnostics;

namespace NotifyRail.Api.Telemetry;

public static class NotifyRailTelemetry
{
    public const string ActivitySourceName = "NotifyRail.Api";

    public const string MessageIntakeActivity = "notifyrail.message.intake";

    public const string ApiClientIdTag = "notifyrail.api_client.id";
    public const string MessageIdTag = "notifyrail.message.id";
    public const string DeliveryIdTag = "notifyrail.delivery.id";
    public const string WebhookEventIdTag = "notifyrail.webhook_event.id";
    public const string WebhookAttemptIdTag = "notifyrail.webhook_attempt.id";
    public const string DeliveryCountTag = "notifyrail.delivery.count";
    public const string RecipientTag = "notifyrail.recipient.masked";

    internal static readonly ActivitySource ActivitySource = new(ActivitySourceName);

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
