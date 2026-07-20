namespace NotifyRail.Api.Features.Webhooks.Persistence;

public sealed class WebhookSecret
{
    private WebhookSecret()
    {
    }

    public static WebhookSecret Create(
        Guid apiClientId,
        byte[] protectedValue,
        DateTimeOffset createdAt)
    {
        return new WebhookSecret
        {
            Id = Guid.NewGuid(),
            ApiClientId = apiClientId,
            ProtectedValue = protectedValue,
            CreatedAt = createdAt,
        };
    }

    public Guid Id { get; private set; }

    public Guid ApiClientId { get; private set; }

    public byte[] ProtectedValue { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? RetiredAt { get; private set; }

    public void Retire(DateTimeOffset retiredAt)
    {
        if (retiredAt < CreatedAt)
        {
            throw new ArgumentOutOfRangeException(
                nameof(retiredAt),
                "Webhook Secret cannot retire before it was created.");
        }

        RetiredAt = retiredAt;
    }
}
