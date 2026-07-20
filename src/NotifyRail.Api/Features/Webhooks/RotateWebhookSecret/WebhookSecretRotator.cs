using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NotifyRail.Api.Features.Webhooks.Persistence;
using NotifyRail.Api.Features.Webhooks.Secrets;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.Webhooks.RotateWebhookSecret;

public sealed class WebhookSecretRotator(
    NotifyRailDbContext dbContext,
    IWebhookSecretProtector secretProtector,
    TimeProvider timeProvider,
    IOptions<WebhookOptions> options)
{
    private readonly TimeSpan _overlap = options.Value.SecretRotationOverlap;

    public async Task<RotateWebhookSecretResponse?> RotateAsync(
        Guid apiClientId,
        CancellationToken cancellationToken)
    {
        await using var transaction =
            await dbContext.Database.BeginTransactionAsync(cancellationToken);
        var apiClient = await dbContext.ApiClients
            .FromSqlInterpolated(
                $"SELECT * FROM api_clients WHERE id = {apiClientId} FOR UPDATE")
            .SingleOrDefaultAsync(cancellationToken);
        if (apiClient is null)
        {
            return null;
        }

        await dbContext.Database.ExecuteSqlInterpolatedAsync(
            $"SELECT pg_advisory_xact_lock(hashtextextended({apiClientId.ToString()}, 0))",
            cancellationToken);

        var current = await dbContext.WebhookSecrets.SingleOrDefaultAsync(
            secret => secret.ApiClientId == apiClientId && secret.RetiredAt == null,
            cancellationToken);
        if (current is null)
        {
            return null;
        }

        var now = PostgresTimestamp.Normalize(timeProvider.GetUtcNow());
        var overlapExpiresAt = now.Add(_overlap);
        var previouslyRotated = await dbContext.WebhookSecrets
            .Where(secret =>
                secret.ApiClientId == apiClientId
                && secret.RetiredAt != null
                && secret.RetiredAt > now)
            .ToListAsync(cancellationToken);
        foreach (var secret in previouslyRotated)
        {
            secret.Retire(now);
        }

        current.Retire(overlapExpiresAt);
        await dbContext.SaveChangesAsync(cancellationToken);

        var plaintextSecret = WebhookSecretCredential.Generate();
        dbContext.WebhookSecrets.Add(WebhookSecret.Create(
            apiClientId,
            secretProtector.Protect(plaintextSecret),
            now));
        await dbContext.SaveChangesAsync(cancellationToken);
        await transaction.CommitAsync(cancellationToken);

        return new RotateWebhookSecretResponse(
            plaintextSecret,
            now,
            overlapExpiresAt);
    }
}
