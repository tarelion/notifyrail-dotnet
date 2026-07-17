using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NotifyRail.Api.Authentication;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Tests;

public sealed class ApiKeyLifecycleEndpointIntegrationTests : IDisposable
{
    private const string OperatorCredential = "test-operator-credential-with-high-entropy";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly MutableTimeProvider _timeProvider = new();

    public ApiKeyLifecycleEndpointIntegrationTests()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithoutHostedServices()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting("Authentication:Operator:Credential", OperatorCredential);
                builder.ConfigureServices(services =>
                {
                    services.RemoveAll<TimeProvider>();
                    services.AddSingleton<TimeProvider>(_timeProvider);
                    services.AddSingleton<IStartupFilter, ApiClientTestEndpointStartupFilter>();
                });
            });
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    [Fact]
    public async Task CreateApiKey_ReturnsASecondActiveCredential_WhenOperatorIsAuthenticated()
    {
        await EnsureDatabaseReadyAsync();

        using var client = CreateOperatorClient();
        var apiClient = await CreateApiClientAsync(client);

        Assert.NotEqual(Guid.Empty, apiClient.ApiKeyId);
        Assert.Equal(apiClient.ApiKey[..16], apiClient.DisplayPrefix);

        using var response = await client.PostAsJsonAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/api-keys",
            new { expires_at = (DateTimeOffset?)null });
        var created = await response.Content.ReadFromJsonAsync<CreateApiKeyResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.ApiKeyId);
        Assert.NotEqual(apiClient.ApiKey, created.ApiKey);
        Assert.StartsWith("nrk_", created.ApiKey, StringComparison.Ordinal);
        Assert.Equal(created.ApiKey[..16], created.DisplayPrefix);
        Assert.Null(created.ExpiresAt);
        Assert.Equal(
            $"/management/api-clients/{apiClient.ApiClientId}/api-keys/{created.ApiKeyId}",
            response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task ListApiKeys_ReturnsLifecycleMetadataWithoutSecrets()
    {
        await EnsureDatabaseReadyAsync();

        using var client = CreateOperatorClient();
        var apiClient = await CreateApiClientAsync(client);
        var expiresAt = DateTimeOffset.UtcNow.AddDays(30);
        using var createResponse = await client.PostAsJsonAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/api-keys",
            new { expires_at = expiresAt });
        var secondKey = await createResponse.Content.ReadFromJsonAsync<CreateApiKeyResponse>();
        Assert.NotNull(secondKey);

        using var response = await client.GetAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/api-keys");
        var body = await response.Content.ReadAsStringAsync();
        var listed = await response.Content.ReadFromJsonAsync<ListApiKeysResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(listed);
        Assert.Equal(2, listed.ApiKeys.Count);
        Assert.All(listed.ApiKeys, key =>
        {
            Assert.NotEqual(Guid.Empty, key.ApiKeyId);
            Assert.StartsWith("nrk_", key.DisplayPrefix, StringComparison.Ordinal);
            Assert.NotEqual(default, key.CreatedAt);
            Assert.Null(key.LastUsedAt);
            Assert.Null(key.RevokedAt);
        });
        Assert.Equal(secondKey.ExpiresAt, listed.ApiKeys.Single(key => key.ApiKeyId == secondKey.ApiKeyId).ExpiresAt);
        Assert.DoesNotContain(apiClient.ApiKey, body, StringComparison.Ordinal);
        Assert.DoesNotContain(secondKey.ApiKey, body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task UseApiKey_RecordsLastUseThroughAuthenticatedHttp()
    {
        await EnsureDatabaseReadyAsync();

        using var operatorClient = CreateOperatorClient();
        var apiClient = await CreateApiClientAsync(operatorClient);

        using var apiKeyClient = _factory.CreateClient();
        apiKeyClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", apiClient.ApiKey);
        using var authenticationResponse = await apiKeyClient.GetAsync("/__tests/api-client");

        Assert.Equal(HttpStatusCode.NoContent, authenticationResponse.StatusCode);

        using var metadataResponse = await operatorClient.GetAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/api-keys");
        var listed = await metadataResponse.Content.ReadFromJsonAsync<ListApiKeysResponse>();

        Assert.NotNull(listed);
        Assert.Equal(_timeProvider.GetUtcNow(), Assert.Single(listed.ApiKeys).LastUsedAt);
    }

    [Fact]
    public async Task RevokeApiKey_PermanentlyRejectsOnlyThatCredential()
    {
        await EnsureDatabaseReadyAsync();

        using var operatorClient = CreateOperatorClient();
        var apiClient = await CreateApiClientAsync(operatorClient);
        using var createResponse = await operatorClient.PostAsJsonAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/api-keys",
            new { expires_at = (DateTimeOffset?)null });
        var secondKey = await createResponse.Content.ReadFromJsonAsync<CreateApiKeyResponse>();
        Assert.NotNull(secondKey);

        var initialKeyId = await FindApiKeyIdAsync(
            operatorClient,
            apiClient.ApiClientId,
            apiClient.ApiKey[..16]);
        using var revokeResponse = await operatorClient.PostAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/api-keys/{initialKeyId}/revoke",
            content: null);

        Assert.Equal(HttpStatusCode.NoContent, revokeResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await AuthenticateAsync(apiClient.ApiKey));
        Assert.Equal(HttpStatusCode.NoContent, await AuthenticateAsync(secondKey.ApiKey));

        var firstRevokedAt = (await ListApiKeysAsync(operatorClient, apiClient.ApiClientId))
            .Single(key => key.ApiKeyId == initialKeyId)
            .RevokedAt;
        Assert.Equal(_timeProvider.GetUtcNow(), firstRevokedAt);

        _timeProvider.Advance(TimeSpan.FromHours(1));
        using var repeatedResponse = await operatorClient.PostAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/api-keys/{initialKeyId}/revoke",
            content: null);

        Assert.Equal(HttpStatusCode.NoContent, repeatedResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, await AuthenticateAsync(apiClient.ApiKey));
        Assert.Equal(
            firstRevokedAt,
            (await ListApiKeysAsync(operatorClient, apiClient.ApiClientId))
                .Single(key => key.ApiKeyId == initialKeyId)
                .RevokedAt);
    }

    [Fact]
    public async Task ExpiredApiKey_FailsAuthenticationWithoutUpdatingLastUse()
    {
        await EnsureDatabaseReadyAsync();

        using var operatorClient = CreateOperatorClient();
        var apiClient = await CreateApiClientAsync(operatorClient);
        var expiresAt = _timeProvider.GetUtcNow().AddMinutes(5);
        using var createResponse = await operatorClient.PostAsJsonAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/api-keys",
            new { expires_at = expiresAt });
        var expiringKey = await createResponse.Content.ReadFromJsonAsync<CreateApiKeyResponse>();
        Assert.NotNull(expiringKey);

        Assert.Equal(HttpStatusCode.NoContent, await AuthenticateAsync(expiringKey.ApiKey));
        var lastUsedAt = (await ListApiKeysAsync(operatorClient, apiClient.ApiClientId))
            .Single(key => key.ApiKeyId == expiringKey.ApiKeyId)
            .LastUsedAt;

        _timeProvider.Advance(TimeSpan.FromMinutes(5));

        Assert.Equal(HttpStatusCode.Unauthorized, await AuthenticateAsync(expiringKey.ApiKey));
        Assert.Equal(
            lastUsedAt,
            (await ListApiKeysAsync(operatorClient, apiClient.ApiClientId))
                .Single(key => key.ApiKeyId == expiringKey.ApiKeyId)
                .LastUsedAt);

        _timeProvider.Advance(TimeSpan.FromDays(1));
        Assert.Equal(HttpStatusCode.Unauthorized, await AuthenticateAsync(expiringKey.ApiKey));
    }

    private HttpClient CreateOperatorClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Operator", OperatorCredential);
        return client;
    }

    private static async Task<CreateApiClientResponse> CreateApiClientAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/management/api-clients",
            new { name = $"API Key Lifecycle {Guid.NewGuid()}" });
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<CreateApiClientResponse>())!;
    }

    private async Task<HttpStatusCode> AuthenticateAsync(string apiKey)
    {
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("ApiKey", apiKey);
        using var response = await client.GetAsync("/__tests/api-client");
        return response.StatusCode;
    }

    private static async Task<Guid> FindApiKeyIdAsync(
        HttpClient client,
        Guid apiClientId,
        string displayPrefix)
    {
        return (await ListApiKeysAsync(client, apiClientId))
            .Single(key => key.DisplayPrefix == displayPrefix)
            .ApiKeyId;
    }

    private static async Task<IReadOnlyList<ApiKeyMetadataResponse>> ListApiKeysAsync(
        HttpClient client,
        Guid apiClientId)
    {
        using var response = await client.GetAsync(
            $"/management/api-clients/{apiClientId}/api-keys");
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<ListApiKeysResponse>())!.ApiKeys;
    }

    private async Task EnsureDatabaseReadyAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    private sealed record CreateApiClientResponse(
        [property: JsonPropertyName("api_client_id")] Guid ApiClientId,
        [property: JsonPropertyName("api_key_id")] Guid ApiKeyId,
        [property: JsonPropertyName("api_key")] string ApiKey,
        [property: JsonPropertyName("display_prefix")] string DisplayPrefix);

    private sealed record CreateApiKeyResponse(
        [property: JsonPropertyName("api_key_id")] Guid ApiKeyId,
        [property: JsonPropertyName("api_key")] string ApiKey,
        [property: JsonPropertyName("display_prefix")] string DisplayPrefix,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt);

    private sealed record ListApiKeysResponse(
        [property: JsonPropertyName("api_keys")] IReadOnlyList<ApiKeyMetadataResponse> ApiKeys);

    private sealed record ApiKeyMetadataResponse(
        [property: JsonPropertyName("api_key_id")] Guid ApiKeyId,
        [property: JsonPropertyName("display_prefix")] string DisplayPrefix,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("last_used_at")] DateTimeOffset? LastUsedAt,
        [property: JsonPropertyName("expires_at")] DateTimeOffset? ExpiresAt,
        [property: JsonPropertyName("revoked_at")] DateTimeOffset? RevokedAt);

    private sealed class ApiClientTestEndpointStartupFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next)
        {
            return app =>
            {
                app.Use(async (context, continuePipeline) =>
                {
                    if (context.Request.Path != "/__tests/api-client")
                    {
                        await continuePipeline();
                        return;
                    }

                    var result = await context.AuthenticateAsync(ApiClientAuthenticationHandler.SchemeName);
                    context.Response.StatusCode = result.Succeeded
                        ? StatusCodes.Status204NoContent
                        : StatusCodes.Status401Unauthorized;
                });
                next(app);
            };
        }
    }

    private sealed class MutableTimeProvider : TimeProvider
    {
        private DateTimeOffset _utcNow;

        public MutableTimeProvider()
        {
            var utcNow = DateTimeOffset.UtcNow;
            _utcNow = utcNow.AddTicks(-(utcNow.Ticks % 10));
        }

        public override DateTimeOffset GetUtcNow() => _utcNow;

        public void Advance(TimeSpan duration)
        {
            _utcNow += duration;
        }
    }
}
