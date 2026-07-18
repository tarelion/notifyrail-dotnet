using Microsoft.EntityFrameworkCore;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.Webhooks.DisableWebhookEndpoint;

public sealed class WebhookEndpointDisabler(
    NotifyRailDbContext dbContext,
    TimeProvider timeProvider)
{
    public async Task<bool> DisableAsync(
        Guid apiClientId,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var apiClient = await dbContext.ApiClients
            .FromSqlInterpolated($"SELECT * FROM api_clients WHERE id = {apiClientId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (apiClient is null)
        {
            return false;
        }

        var endpoint = await dbContext.WebhookEndpoints
            .SingleOrDefaultAsync(
                candidate => candidate.ApiClientId == apiClientId && candidate.IsEnabled,
                cancellationToken);
        if (endpoint is not null)
        {
            endpoint.Disable(timeProvider.GetUtcNow());
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }
}
