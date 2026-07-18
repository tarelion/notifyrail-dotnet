namespace NotifyRail.Api.Features.Webhooks.Queue;

public sealed record WebhookJob(WebhookClaim Claim, WebhookRequest Request);
