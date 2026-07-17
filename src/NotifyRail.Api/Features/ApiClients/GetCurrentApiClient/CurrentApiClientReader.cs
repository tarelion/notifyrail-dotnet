using Microsoft.EntityFrameworkCore;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.ApiClients.GetCurrentApiClient;

public sealed class CurrentApiClientReader(NotifyRailDbContext dbContext)
{
    public Task<GetCurrentApiClientResponse?> ReadAsync(
        Guid apiClientId,
        CancellationToken cancellationToken)
    {
        return dbContext.ApiClients
            .AsNoTracking()
            .Where(apiClient => apiClient.Id == apiClientId)
            .Select(apiClient => new GetCurrentApiClientResponse(apiClient.Id, apiClient.Name))
            .SingleOrDefaultAsync(cancellationToken);
    }
}
