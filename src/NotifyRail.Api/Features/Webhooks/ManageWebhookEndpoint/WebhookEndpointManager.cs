using Microsoft.EntityFrameworkCore;
using NotifyRail.Api.Features.ApiClients.Persistence;
using NotifyRail.Api.Features.Webhooks.Persistence;
using NotifyRail.Api.Features.Webhooks.Secrets;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.Webhooks.ManageWebhookEndpoint;

public sealed class WebhookEndpointManager(
    NotifyRailDbContext dbContext,
    IWebhookSecretProtector secretProtector,
    TimeProvider timeProvider)
{
    public async Task<RegisterWebhookEndpointResponse?> RegisterAsync(
        Guid apiClientId,
        string url,
        CancellationToken cancellationToken)
    {
        await using var transaction = await dbContext.Database.BeginTransactionAsync(cancellationToken);

        var apiClient = await dbContext.ApiClients
            .FromSqlInterpolated($"SELECT * FROM api_clients WHERE id = {apiClientId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (apiClient is null)
        {
            return null;
        }

        var now = timeProvider.GetUtcNow();
        var current = await dbContext.WebhookEndpoints
            .SingleOrDefaultAsync(
                endpoint => endpoint.ApiClientId == apiClientId && endpoint.IsEnabled,
                cancellationToken);
        if (current is not null)
        {
            current.Disable(now);
            await dbContext.SaveChangesAsync(cancellationToken);
        }

        var secretExists = await dbContext.WebhookSecrets
            .AnyAsync(secret => secret.ApiClientId == apiClientId, cancellationToken);
        string? plaintextSecret = null;
        if (!secretExists)
        {
            plaintextSecret = WebhookSecretCredential.Generate();
            dbContext.WebhookSecrets.Add(WebhookSecret.Create(
                apiClientId,
                secretProtector.Protect(plaintextSecret),
                now));
        }

        var endpoint = WebhookEndpoint.Create(apiClientId, url, now);
        dbContext.WebhookEndpoints.Add(endpoint);
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new RegisterWebhookEndpointResponse(
            endpoint.Id,
            endpoint.ApiClientId,
            endpoint.Url,
            endpoint.IsEnabled,
            endpoint.CreatedAt,
            endpoint.UpdatedAt,
            plaintextSecret);
    }

    public async Task<WebhookEndpointResponse?> InspectAsync(
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
            .Select(endpoint => new WebhookEndpointResponse(
                endpoint.Id,
                endpoint.ApiClientId,
                endpoint.Url,
                endpoint.IsEnabled,
                endpoint.CreatedAt,
                endpoint.UpdatedAt,
                endpoint.DisabledAt))
            .FirstOrDefaultAsync(cancellationToken);
    }

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
