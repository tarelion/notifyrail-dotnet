namespace NotifyRail.Api.Features.Webhooks.Persistence;

public sealed class WebhookEndpoint
{
    private WebhookEndpoint()
    {
    }

    public static WebhookEndpoint Create(
        Guid apiClientId,
        string url,
        DateTimeOffset createdAt)
    {
        return new WebhookEndpoint
        {
            Id = Guid.NewGuid(),
            ApiClientId = apiClientId,
            Url = url,
            IsEnabled = true,
            CreatedAt = createdAt,
            UpdatedAt = createdAt,
        };
    }

    public void Disable(DateTimeOffset disabledAt)
    {
        if (!IsEnabled)
        {
            return;
        }

        IsEnabled = false;
        DisabledAt = disabledAt;
        UpdatedAt = disabledAt;
    }

    public Guid Id { get; private set; }

    public Guid ApiClientId { get; private set; }

    public string Url { get; private set; } = null!;

    public bool IsEnabled { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? DisabledAt { get; private set; }
}
