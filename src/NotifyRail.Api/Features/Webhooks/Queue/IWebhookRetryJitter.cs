namespace NotifyRail.Api.Features.Webhooks.Queue;

public interface IWebhookRetryJitter
{
    double NextDouble();
}

internal sealed class RandomWebhookRetryJitter : IWebhookRetryJitter
{
    public double NextDouble()
    {
        return Random.Shared.NextDouble();
    }
}
