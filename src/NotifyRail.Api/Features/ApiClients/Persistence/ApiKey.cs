namespace NotifyRail.Api.Features.ApiClients.Persistence;

public sealed class ApiKey
{
    private ApiKey()
    {
    }

    public static ApiKey Create(
        Guid apiClientId,
        string lookupId,
        byte[] verificationHash,
        string displayPrefix,
        DateTimeOffset createdAt)
    {
        return new ApiKey
        {
            Id = Guid.NewGuid(),
            ApiClientId = apiClientId,
            LookupId = lookupId,
            VerificationHash = verificationHash,
            DisplayPrefix = displayPrefix,
            CreatedAt = createdAt,
        };
    }

    public Guid Id { get; private set; }

    public Guid ApiClientId { get; private set; }

    public string LookupId { get; private set; } = null!;

    public byte[] VerificationHash { get; private set; } = null!;

    public string DisplayPrefix { get; private set; } = null!;

    public DateTimeOffset CreatedAt { get; private set; }

    public DateTimeOffset? LastUsedAt { get; private set; }

    public DateTimeOffset? ExpiresAt { get; private set; }

    public DateTimeOffset? RevokedAt { get; private set; }
}
