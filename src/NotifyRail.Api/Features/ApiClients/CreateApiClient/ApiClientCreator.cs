using NotifyRail.Api.Features.ApiClients.Persistence;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Features.ApiClients.CreateApiClient;

public sealed class ApiClientCreator(NotifyRailDbContext dbContext, TimeProvider timeProvider)
{
    public async Task<CreateApiClientResponse> CreateAsync(
        string name,
        CancellationToken cancellationToken)
    {
        var createdAt = timeProvider.GetUtcNow();
        var apiClient = ApiClient.Create(name.Trim(), createdAt);
        var credential = ApiKeyCredential.Generate();
        var apiKey = ApiKey.Create(
            apiClient.Id,
            credential.LookupId,
            credential.VerificationHash,
            credential.DisplayPrefix,
            createdAt);

        dbContext.ApiClients.Add(apiClient);
        dbContext.ApiKeys.Add(apiKey);
        await dbContext.SaveChangesAsync(cancellationToken);

        return new CreateApiClientResponse(
            apiClient.Id,
            apiClient.Name,
            credential.Plaintext,
            apiClient.CreatedAt);
    }
}
