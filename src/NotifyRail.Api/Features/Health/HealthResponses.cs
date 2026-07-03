namespace NotifyRail.Api.Features.Health;

public sealed record HealthResponse(string Status);

public sealed record ReadinessResponse(string Status);
