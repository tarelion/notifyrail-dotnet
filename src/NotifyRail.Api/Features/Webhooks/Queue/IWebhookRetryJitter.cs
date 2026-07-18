namespace NotifyRail.Api.Features.Webhooks.Queue;

public interface IWebhookRetryJitter
{
    double NextUnitIntervalSample();
}

internal sealed class RandomWebhookRetryJitter : IWebhookRetryJitter
{
    public double NextUnitIntervalSample()
    {
        return Random.Shared.NextDouble();
    }
}
