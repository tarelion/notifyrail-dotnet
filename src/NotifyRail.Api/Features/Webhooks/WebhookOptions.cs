namespace NotifyRail.Api.Features.Webhooks;

public sealed class WebhookOptions
{
    public const string SectionName = "Webhooks";

    public static readonly TimeSpan MaximumSecretRotationOverlap = TimeSpan.FromDays(30);

    public bool AllowLocalhostEndpoints { get; init; }

    public TimeSpan SecretRotationOverlap { get; init; } = TimeSpan.FromHours(24);
}
