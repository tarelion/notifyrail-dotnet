using Microsoft.EntityFrameworkCore;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.ApiClients.DisableApiClient;

public sealed class ApiClientDisabler(NotifyRailDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<bool> DisableAsync(Guid apiClientId, CancellationToken cancellationToken)
    {
        var apiClient = await dbContext.ApiClients.SingleOrDefaultAsync(
            candidate => candidate.Id == apiClientId,
            cancellationToken);
        if (apiClient is null)
        {
            return false;
        }

        apiClient.Disable(timeProvider.GetUtcNow());
        await dbContext.SaveChangesAsync(cancellationToken);

        return true;
    }
}
