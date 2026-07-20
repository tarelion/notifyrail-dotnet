using Microsoft.EntityFrameworkCore;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.Webhooks.InspectWebhookEndpoint;

public sealed class WebhookEndpointReader(NotifyRailDbContext dbContext)
{
    public async Task<InspectWebhookEndpointResponse?> ReadAsync(
        Guid apiClientId,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var apiClient = await dbContext.ApiClients
            .FromSqlInterpolated(
                $"SELECT * FROM api_clients WHERE id = {apiClientId} FOR SHARE")
            .AsNoTracking()
            .SingleOrDefaultAsync(cancellationToken);
        if (apiClient is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var endpoint = await dbContext.WebhookEndpoints
            .AsNoTracking()
            .Where(endpoint => endpoint.ApiClientId == apiClientId)
            .OrderByDescending(endpoint => endpoint.IsEnabled)
            .ThenByDescending(endpoint => endpoint.CreatedAt)
            .ThenByDescending(endpoint => endpoint.Id)
            .Select(endpoint => new InspectWebhookEndpointResponse(
                endpoint.Id,
                endpoint.ApiClientId,
                endpoint.Url,
                endpoint.IsEnabled,
                endpoint.CreatedAt,
                endpoint.UpdatedAt,
                endpoint.DisabledAt,
                null,
                null))
            .FirstOrDefaultAsync(cancellationToken);
        if (endpoint is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var currentSecretCreatedAt = await dbContext.WebhookSecrets
            .AsNoTracking()
            .Where(secret => secret.ApiClientId == apiClientId && secret.RetiredAt == null)
            .Select(secret => (DateTimeOffset?)secret.CreatedAt)
            .SingleOrDefaultAsync(cancellationToken);
        var overlapExpiresAt = await dbContext.WebhookSecrets
            .AsNoTracking()
            .Where(secret => secret.ApiClientId == apiClientId && secret.RetiredAt != null)
            .OrderByDescending(secret => secret.RetiredAt)
            .ThenByDescending(secret => secret.CreatedAt)
            .ThenByDescending(secret => secret.Id)
            .Select(secret => secret.RetiredAt)
            .FirstOrDefaultAsync(cancellationToken);

        var response = endpoint with
        {
            WebhookSecretCreatedAt = currentSecretCreatedAt,
            WebhookSecretOverlapExpiresAt = overlapExpiresAt,
        };
        await transaction.CommitAsync(cancellationToken);
        return response;
    }
}
