using NotifyRail.Api.Telemetry;

namespace NotifyRail.Api.Features.Webhooks.Queue;

public sealed record WebhookRequest(
    Guid EventId,
    TelemetryCorrelation Correlation,
    string EndpointUrl,
    string Body,
    byte[] ProtectedSecret);
