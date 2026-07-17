using Microsoft.EntityFrameworkCore;
using NotifyRail.Api.Features.ApiClients.Persistence;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.ApiClients.CreateApiKey;

public sealed class ApiKeyCreator(NotifyRailDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<CreateApiKeyResponse?> CreateAsync(
        Guid apiClientId,
        DateTimeOffset? expiresAt,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.ApiClients.AnyAsync(
                apiClient => apiClient.Id == apiClientId,
                cancellationToken))
        {
            return null;
        }

        var createdAt = timeProvider.GetUtcNow();
        var credential = ApiKeyCredential.Generate();
        var apiKey = ApiKey.Create(
            apiClientId,
            credential.LookupId,
            credential.VerificationHash,
            credential.DisplayPrefix,
            createdAt,
            expiresAt is null ? null : NormalizeTimestamp(expiresAt.Value));

        dbContext.ApiKeys.Add(apiKey);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateApiKeyResponse(
            apiKey.Id,
            credential.Plaintext,
            apiKey.DisplayPrefix,
            apiKey.CreatedAt,
            apiKey.ExpiresAt);
    }

    private static DateTimeOffset NormalizeTimestamp(DateTimeOffset value)
    {
        var utcValue = value.ToUniversalTime();
        return utcValue.AddTicks(-(utcValue.Ticks % 10));
    }
}
