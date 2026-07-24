namespace NotifyRail.Api.Telemetry;

public sealed record TelemetryCorrelation(
    Guid ApiClientId,
    Guid MessageId,
    Guid DeliveryId,
    string? SourceTraceParent);
