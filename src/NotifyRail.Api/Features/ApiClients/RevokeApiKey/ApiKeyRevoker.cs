using Microsoft.EntityFrameworkCore;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.ApiClients.RevokeApiKey;

public sealed class ApiKeyRevoker(NotifyRailDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<bool> RevokeAsync(
        Guid apiClientId,
        Guid apiKeyId,
        CancellationToken cancellationToken)
    {
        var apiKey = await dbContext.ApiKeys.SingleOrDefaultAsync(
            candidate => candidate.Id == apiKeyId && candidate.ApiClientId == apiClientId,
            cancellationToken);
        if (apiKey is null)
        {
            return false;
        }

        apiKey.Revoke(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);
        return true;
    }
}
