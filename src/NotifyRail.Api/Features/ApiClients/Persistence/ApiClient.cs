namespace NotifyRail.Api.Features.ApiClients.Persistence;

public sealed class ApiClient
{
    public static readonly Guid LegacyId = Guid.Parse("00000000-0000-0000-0000-000000000001");

    private ApiClient()
    {
    }

    public static ApiClient Create(string name, DateTimeOffset createdAt)
    {
        return new ApiClient
        {
            Id = Guid.NewGuid(),
            Name = name,
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

    public string Name { get; private set; } = null!;

    public bool IsEnabled { get; private set; }

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset UpdatedAt { get; private set; }

    public DateTimeOffset? DisabledAt { get; private set; }
}
