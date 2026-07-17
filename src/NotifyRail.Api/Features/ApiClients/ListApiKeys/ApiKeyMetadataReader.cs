using Microsoft.EntityFrameworkCore;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.ApiClients.ListApiKeys;

public sealed class ApiKeyMetadataReader(NotifyRailDbContext dbContext)
{
    public async Task<ListApiKeysResponse?> ReadAsync(
        Guid apiClientId,
        CancellationToken cancellationToken)
    {
        if (!await dbContext.ApiClients.AsNoTracking().AnyAsync(
                apiClient => apiClient.Id == apiClientId,
                cancellationToken))
        {
            return null;
        }

        var apiKeys = await dbContext.ApiKeys
            .AsNoTracking()
            .Where(apiKey => apiKey.ApiClientId == apiClientId)
            .OrderBy(apiKey => apiKey.CreatedAt)
            .ThenBy(apiKey => apiKey.Id)
            .Select(apiKey => new ApiKeyMetadataResponse(
                apiKey.Id,
                apiKey.DisplayPrefix,
                apiKey.CreatedAt,
                apiKey.LastUsedAt,
                apiKey.ExpiresAt,
                apiKey.RevokedAt))
            .ToListAsync(cancellationToken);

        return new ListApiKeysResponse(apiKeys);
    }
}
