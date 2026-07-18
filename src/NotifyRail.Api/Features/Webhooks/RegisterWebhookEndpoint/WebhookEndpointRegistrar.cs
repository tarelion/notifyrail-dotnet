using Microsoft.EntityFrameworkCore;
using NotifyRail.Api.Features.Webhooks.Persistence;
using NotifyRail.Api.Features.Webhooks.Secrets;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.Webhooks.RegisterWebhookEndpoint;

public sealed class WebhookEndpointRegistrar(
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
}
