using Microsoft.EntityFrameworkCore;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.Webhooks.InspectWebhookEndpoint;

public sealed class WebhookEndpointReader(NotifyRailDbContext dbContext)
{
    public async Task<InspectWebhookEndpointResponse?> ReadAsync(
        Guid apiClientId,
        CancellationToken cancellationToken)
    {
        var apiClientExists = await dbContext.ApiClients
            .AnyAsync(apiClient => apiClient.Id == apiClientId, cancellationToken);
        if (!apiClientExists)
        {
            return null;
        }

        return await dbContext.WebhookEndpoints
            .AsNoTracking()
            .Where(endpoint => endpoint.ApiClientId == apiClientId)
            .OrderByDescending(endpoint => endpoint.CreatedAt)
            .ThenByDescending(endpoint => endpoint.Id)
            .Select(endpoint => new InspectWebhookEndpointResponse(
                endpoint.Id,
                endpoint.ApiClientId,
                endpoint.Url,
                endpoint.IsEnabled,
                endpoint.CreatedAt,
                endpoint.UpdatedAt,
                endpoint.DisabledAt))
            .FirstOrDefaultAsync(cancellationToken);
    }
}
