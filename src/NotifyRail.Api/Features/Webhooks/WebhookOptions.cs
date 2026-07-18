namespace NotifyRail.Api.Features.Webhooks;

public sealed class WebhookOptions
{
    public const string SectionName = "Webhooks";

    public bool AllowLocalhostEndpoints { get; init; }
}
