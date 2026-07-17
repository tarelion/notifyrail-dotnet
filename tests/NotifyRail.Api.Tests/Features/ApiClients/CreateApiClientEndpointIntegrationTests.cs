using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json.Serialization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotifyRail.Api.Infrastructure.Persistence;
using NotifyRail.Api.Authentication;

namespace NotifyRail.Api.Tests;

public sealed class CreateApiClientEndpointIntegrationTests : IDisposable
{
    private const string OperatorCredential = "test-operator-credential-with-high-entropy";

    private readonly WebApplicationFactory<Program> _factory;

    public CreateApiClientEndpointIntegrationTests()
    {
        _factory = new WebApplicationFactory<Program>()
            .WithoutHostedServices()
            .WithWebHostBuilder(builder => builder.UseSetting(
                "Authentication:Operator:Credential",
                OperatorCredential));
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    [Fact]
    public async Task CreateApiClient_ReturnsInitialApiKey_WhenOperatorIsAuthenticated()
    {
        await EnsureDatabaseReadyAsync();

        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Operator", OperatorCredential);

        using var response = await client.PostAsJsonAsync(
            "/management/api-clients",
            new { name = "Shipping Service" });
        var created = await response.Content.ReadFromJsonAsync<CreateApiClientResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(created);
        Assert.NotEqual(Guid.Empty, created.ApiClientId);
        Assert.Equal("Shipping Service", created.Name);
        Assert.StartsWith("nrk_", created.ApiKey, StringComparison.Ordinal);
        Assert.Equal($"/management/api-clients/{created.ApiClientId}", response.Headers.Location?.OriginalString);
    }

    [Fact]
    public async Task CreateApiClient_PersistsOnlyVerificationValueAndDisplaySafeMetadata()
    {
        await EnsureDatabaseReadyAsync();

        using var client = CreateOperatorClient();
        using var response = await client.PostAsJsonAsync(
            "/management/api-clients",
            new { name = "Billing Service" });
        var created = await response.Content.ReadFromJsonAsync<CreateApiClientResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(created);

        var parts = created.ApiKey.Split('_', 3);
        Assert.Equal(3, parts.Length);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
        var persistedKey = await dbContext.ApiKeys
            .AsNoTracking()
            .SingleAsync(apiKey => apiKey.ApiClientId == created.ApiClientId);

        Assert.Equal(parts[1], persistedKey.LookupId);
        Assert.Equal(created.ApiKey[..16], persistedKey.DisplayPrefix);
        Assert.Equal(
            SHA256.HashData(Encoding.UTF8.GetBytes(parts[2])),
            persistedKey.VerificationHash);
        Assert.DoesNotContain(parts[2], persistedKey.DisplayPrefix, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateApiClient_ReturnsUnauthorized_WithoutOperatorCredential()
    {
        await EnsureDatabaseReadyAsync();

        using var client = _factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/management/api-clients",
            new { name = "Unauthorized Service" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task CreateApiClient_ReturnsBadRequest_WhenNameIsBlank()
    {
        await EnsureDatabaseReadyAsync();

        using var client = CreateOperatorClient();
        using var response = await client.PostAsJsonAsync(
            "/management/api-clients",
            new { name = "   " });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task Authentication_ExposesSeparateApiClientAndOperatorSchemesAndPolicies()
    {
        var schemeProvider = _factory.Services.GetRequiredService<IAuthenticationSchemeProvider>();
        var policyProvider = _factory.Services.GetRequiredService<IAuthorizationPolicyProvider>();

        var apiClientScheme = await schemeProvider.GetSchemeAsync("ApiClient");
        var operatorScheme = await schemeProvider.GetSchemeAsync("Operator");
        var apiClientPolicy = await policyProvider.GetPolicyAsync(AuthenticationPolicies.ApiClient);
        var operatorPolicy = await policyProvider.GetPolicyAsync(AuthenticationPolicies.Operator);

        Assert.NotNull(apiClientScheme);
        Assert.NotNull(operatorScheme);
        Assert.NotNull(apiClientPolicy);
        Assert.NotNull(operatorPolicy);
        Assert.Contains("ApiClient", apiClientPolicy.AuthenticationSchemes);
        Assert.Contains("Operator", operatorPolicy.AuthenticationSchemes);
    }

    [Fact]
    public async Task CreateApiClient_ReturnsUnauthorized_WhenApiKeyIsUsedAsOperatorCredential()
    {
        await EnsureDatabaseReadyAsync();

        using var operatorClient = CreateOperatorClient();
        using var firstResponse = await operatorClient.PostAsJsonAsync(
            "/management/api-clients",
            new { name = "Existing API Client" });
        var firstClient = await firstResponse.Content.ReadFromJsonAsync<CreateApiClientResponse>();
        Assert.NotNull(firstClient);

        using var apiClient = _factory.CreateClient();
        apiClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", firstClient.ApiKey);
        using var response = await apiClient.PostAsJsonAsync(
            "/management/api-clients",
            new { name = "Escalated API Client" });

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task DisableApiClient_DisablesClient_WhenOperatorIsAuthenticated()
    {
        await EnsureDatabaseReadyAsync();

        using var client = CreateOperatorClient();
        using var createResponse = await client.PostAsJsonAsync(
            "/management/api-clients",
            new { name = "Service To Disable" });
        var created = await createResponse.Content.ReadFromJsonAsync<CreateApiClientResponse>();
        Assert.NotNull(created);

        using var disableResponse = await client.PostAsync(
            $"/management/api-clients/{created.ApiClientId}/disable",
            content: null);

        Assert.Equal(HttpStatusCode.NoContent, disableResponse.StatusCode);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
        var persistedClient = await dbContext.ApiClients
            .AsNoTracking()
            .SingleAsync(apiClient => apiClient.Id == created.ApiClientId);

        Assert.False(persistedClient.IsEnabled);
        Assert.NotNull(persistedClient.DisabledAt);
    }

    [Fact]
    public async Task ApiClientAuthentication_RejectsKeyAfterApiClientIsDisabled()
    {
        await EnsureDatabaseReadyAsync();

        using var client = CreateOperatorClient();
        using var createResponse = await client.PostAsJsonAsync(
            "/management/api-clients",
            new { name = "Authenticated Service" });
        var created = await createResponse.Content.ReadFromJsonAsync<CreateApiClientResponse>();
        Assert.NotNull(created);

        var authenticated = await AuthenticateApiClientAsync(created.ApiKey);
        Assert.True(authenticated.Succeeded);
        Assert.Equal(
            created.ApiClientId.ToString(),
            authenticated.Principal?.FindFirst("notifyrail:api_client_id")?.Value);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
            var lastUsedAt = await dbContext.ApiKeys
                .Where(apiKey => apiKey.ApiClientId == created.ApiClientId)
                .Select(apiKey => apiKey.LastUsedAt)
                .SingleAsync();
            Assert.NotNull(lastUsedAt);
        }

        using var disableResponse = await client.PostAsync(
            $"/management/api-clients/{created.ApiClientId}/disable",
            content: null);
        Assert.Equal(HttpStatusCode.NoContent, disableResponse.StatusCode);

        var disabled = await AuthenticateApiClientAsync(created.ApiKey);
        Assert.False(disabled.Succeeded);
    }

    private HttpClient CreateOperatorClient()
    {
        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Operator", OperatorCredential);

        return client;
    }

    private async Task<AuthenticateResult> AuthenticateApiClientAsync(string apiKey)
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
        };
        context.Request.Headers.Authorization = $"ApiKey {apiKey}";

        return await context.AuthenticateAsync("ApiClient");
    }

    private async Task EnsureDatabaseReadyAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();

        await dbContext.Database.MigrateAsync();
    }

    private sealed record CreateApiClientResponse(
        [property: JsonPropertyName("api_client_id")] Guid ApiClientId,
        [property: JsonPropertyName("name")] string Name,
        [property: JsonPropertyName("api_key")] string ApiKey,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt);
}
