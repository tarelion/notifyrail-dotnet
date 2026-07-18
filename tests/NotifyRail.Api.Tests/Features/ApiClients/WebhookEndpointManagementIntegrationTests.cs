using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NotifyRail.Api.Infrastructure.Persistence;
using NotifyRail.Api.Features.Webhooks.Secrets;
using NotifyRail.Api.Features.Webhooks.Persistence;
using Npgsql;

namespace NotifyRail.Api.Tests;

public sealed class WebhookEndpointManagementIntegrationTests : IDisposable
{
    private const string OperatorCredential = "test-operator-credential-with-high-entropy";

    private readonly WebApplicationFactory<Program> _factory;

    public WebhookEndpointManagementIntegrationTests()
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
    public async Task RegisterWebhookEndpoint_ReturnsSecretOnce()
    {
        await EnsureDatabaseReadyAsync();

        using var client = CreateOperatorClient();
        var apiClient = await CreateApiClientAsync(client);

        using var registerResponse = await client.PutAsJsonAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/webhook-endpoint",
            new { url = "https://hooks.example.com/notifyrail" });
        var registered = await registerResponse.Content
            .ReadFromJsonAsync<RegisterWebhookEndpointResponse>();

        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);
        Assert.NotNull(registered);
        Assert.NotEqual(Guid.Empty, registered.WebhookEndpointId);
        Assert.Equal(apiClient.ApiClientId, registered.ApiClientId);
        Assert.Equal("https://hooks.example.com/notifyrail", registered.Url);
        Assert.NotNull(registered.WebhookSecret);
        Assert.StartsWith("nrs_", registered.WebhookSecret, StringComparison.Ordinal);

        using var inspectResponse = await client.GetAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/webhook-endpoint");
        using var inspected = JsonDocument.Parse(
            await inspectResponse.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, inspectResponse.StatusCode);
        Assert.False(inspected.RootElement.TryGetProperty("webhook_secret", out _));
        Assert.Equal(
            registered.WebhookEndpointId,
            inspected.RootElement.GetProperty("webhook_endpoint_id").GetGuid());
    }

    [Fact]
    public async Task DisableWebhookEndpoint_LeavesApiClientAvailableForPolling()
    {
        await EnsureDatabaseReadyAsync();

        using var operatorClient = CreateOperatorClient();
        var apiClient = await CreateApiClientAsync(operatorClient);
        using var registerResponse = await operatorClient.PutAsJsonAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/webhook-endpoint",
            new { url = "https://hooks.example.com/notifyrail" });
        Assert.Equal(HttpStatusCode.Created, registerResponse.StatusCode);

        using var disableResponse = await operatorClient.PostAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/webhook-endpoint/disable",
            content: null);
        using var repeatedDisableResponse = await operatorClient.PostAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/webhook-endpoint/disable",
            content: null);
        using var inspectResponse = await operatorClient.GetAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/webhook-endpoint");
        var inspected = await inspectResponse.Content
            .ReadFromJsonAsync<InspectWebhookEndpointResponse>();

        Assert.Equal(HttpStatusCode.NoContent, disableResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, repeatedDisableResponse.StatusCode);
        Assert.Equal(HttpStatusCode.OK, inspectResponse.StatusCode);
        Assert.NotNull(inspected);
        Assert.False(inspected.IsEnabled);
        Assert.NotNull(inspected.DisabledAt);

        using var apiClientHttpClient = _factory.CreateClient();
        apiClientHttpClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", apiClient.ApiKey);
        using var pollResponse = await apiClientHttpClient.GetAsync("/api-client");

        Assert.Equal(HttpStatusCode.OK, pollResponse.StatusCode);
    }

    [Fact]
    public async Task RegisterWebhookEndpoint_ReplacesActiveEndpointWithoutRevealingSecretAgain()
    {
        await EnsureDatabaseReadyAsync();

        using var client = CreateOperatorClient();
        var apiClient = await CreateApiClientAsync(client);
        using var firstResponse = await client.PutAsJsonAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/webhook-endpoint",
            new { url = "https://old.example.com/webhooks" });
        var first = await firstResponse.Content
            .ReadFromJsonAsync<RegisterWebhookEndpointResponse>();
        Assert.NotNull(first);

        using var replacementResponse = await client.PutAsJsonAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/webhook-endpoint",
            new { url = "https://new.example.com/webhooks" });
        using var replacementDocument = JsonDocument.Parse(
            await replacementResponse.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, replacementResponse.StatusCode);
        Assert.False(replacementDocument.RootElement.TryGetProperty("webhook_secret", out _));
        Assert.NotEqual(
            first.WebhookEndpointId,
            replacementDocument.RootElement.GetProperty("webhook_endpoint_id").GetGuid());

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
        var endpoints = await dbContext.WebhookEndpoints
            .AsNoTracking()
            .Where(endpoint => endpoint.ApiClientId == apiClient.ApiClientId)
            .OrderBy(endpoint => endpoint.CreatedAt)
            .ThenBy(endpoint => endpoint.Id)
            .ToListAsync();

        Assert.Equal(2, endpoints.Count);
        Assert.Single(endpoints, endpoint => endpoint.IsEnabled);
        Assert.Contains(endpoints, endpoint =>
            endpoint.Id == first.WebhookEndpointId && !endpoint.IsEnabled);
        Assert.Equal(
            "https://new.example.com/webhooks",
            endpoints.Single(endpoint => endpoint.IsEnabled).Url);
    }

    [Fact]
    public async Task RegisterWebhookEndpoint_PreservesActiveResource_WhenPutIsRetried()
    {
        await EnsureDatabaseReadyAsync();

        using var client = CreateOperatorClient();
        var apiClient = await CreateApiClientAsync(client);
        var route = $"/management/api-clients/{apiClient.ApiClientId}/webhook-endpoint";
        using var firstResponse = await client.PutAsJsonAsync(
            route,
            new { url = "https://stable.example.com/webhooks" });
        var first = await firstResponse.Content
            .ReadFromJsonAsync<RegisterWebhookEndpointResponse>();
        Assert.NotNull(first);

        using var retryResponse = await client.PutAsJsonAsync(
            route,
            new { url = "https://stable.example.com/webhooks" });
        using var retryDocument = JsonDocument.Parse(
            await retryResponse.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, retryResponse.StatusCode);
        Assert.Equal(
            first.WebhookEndpointId,
            retryDocument.RootElement.GetProperty("webhook_endpoint_id").GetGuid());
        Assert.Equal(
            first.CreatedAt,
            retryDocument.RootElement.GetProperty("created_at").GetDateTimeOffset());
        Assert.False(retryDocument.RootElement.TryGetProperty("webhook_secret", out _));

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
        Assert.Equal(
            1,
            await dbContext.WebhookEndpoints.CountAsync(
                endpoint => endpoint.ApiClientId == apiClient.ApiClientId));
    }

    [Fact]
    public async Task InspectWebhookEndpoint_ReturnsLatestDisabledEndpoint_WhenClockDoesNotAdvance()
    {
        using var factory = CreateFactory(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(
                new DateTimeOffset(2026, 7, 18, 12, 0, 0, TimeSpan.Zero)));
        });
        await EnsureDatabaseReadyAsync(factory);

        using var client = CreateOperatorClient(factory);
        var apiClient = await CreateApiClientAsync(client);
        var route = $"/management/api-clients/{apiClient.ApiClientId}/webhook-endpoint";
        using var firstResponse = await client.PutAsJsonAsync(
            route,
            new { url = "https://first.example.com/webhooks" });
        var first = await firstResponse.Content
            .ReadFromJsonAsync<RegisterWebhookEndpointResponse>();
        using var firstDisableResponse = await client.PostAsync(
            $"{route}/disable",
            content: null);
        using var secondResponse = await client.PutAsJsonAsync(
            route,
            new { url = "https://second.example.com/webhooks" });
        var second = await secondResponse.Content
            .ReadFromJsonAsync<RegisterWebhookEndpointResponse>();
        using var secondDisableResponse = await client.PostAsync(
            $"{route}/disable",
            content: null);
        using var inspectResponse = await client.GetAsync(route);
        using var inspected = JsonDocument.Parse(
            await inspectResponse.Content.ReadAsStringAsync());

        Assert.NotNull(first);
        Assert.NotNull(second);
        Assert.Equal(HttpStatusCode.NoContent, firstDisableResponse.StatusCode);
        Assert.Equal(HttpStatusCode.NoContent, secondDisableResponse.StatusCode);
        Assert.True(second.CreatedAt > first.CreatedAt);
        Assert.Equal(
            second.WebhookEndpointId,
            inspected.RootElement.GetProperty("webhook_endpoint_id").GetGuid());
        Assert.Equal(
            "https://second.example.com/webhooks",
            inspected.RootElement.GetProperty("url").GetString());
    }

    [Fact]
    public async Task RegisterWebhookEndpoint_EncryptsSecretThroughWebhookProtectionBoundary()
    {
        await EnsureDatabaseReadyAsync();

        using var client = CreateOperatorClient();
        var apiClient = await CreateApiClientAsync(client);
        using var response = await client.PutAsJsonAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/webhook-endpoint",
            new { url = "https://secure.example.com/webhooks" });
        var registered = await response.Content
            .ReadFromJsonAsync<RegisterWebhookEndpointResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(registered);
        Assert.NotNull(registered.WebhookSecret);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
        var persistedSecret = await dbContext.WebhookSecrets
            .AsNoTracking()
            .SingleAsync(secret => secret.ApiClientId == apiClient.ApiClientId);
        var protector = scope.ServiceProvider.GetRequiredService<IWebhookSecretProtector>();

        Assert.DoesNotContain(
            registered.WebhookSecret,
            Encoding.UTF8.GetString(persistedSecret.ProtectedValue),
            StringComparison.Ordinal);
        Assert.Equal(
            registered.WebhookSecret,
            protector.Unprotect(persistedSecret.ProtectedValue));
    }

    [Theory]
    [InlineData("hooks.example.com/webhooks")]
    [InlineData("ftp://hooks.example.com/webhooks")]
    [InlineData("http://hooks.example.com/webhooks")]
    [InlineData("https://localhost:8443/webhooks")]
    [InlineData("https://localhost./webhooks")]
    [InlineData("https://127.0.0.2/webhooks")]
    [InlineData("https://[::ffff:127.0.0.2]/webhooks")]
    public async Task RegisterWebhookEndpoint_ReturnsBadRequest_WhenUrlViolatesBasicPolicy(
        string url)
    {
        await EnsureDatabaseReadyAsync();

        using var client = CreateOperatorClient();
        var apiClient = await CreateApiClientAsync(client);
        using var response = await client.PutAsJsonAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/webhook-endpoint",
            new { url });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterWebhookEndpoint_AllowsHttpLocalhost_WhenExplicitlyConfigured()
    {
        using var factory = new WebApplicationFactory<Program>()
            .WithoutHostedServices()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(
                    "Authentication:Operator:Credential",
                    OperatorCredential);
                builder.UseSetting("Webhooks:AllowLocalhostEndpoints", "true");
            });
        await EnsureDatabaseReadyAsync(factory);

        using var client = CreateOperatorClient(factory);
        var apiClient = await CreateApiClientAsync(client);
        using var response = await client.PutAsJsonAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/webhook-endpoint",
            new { url = "http://localhost:8081/webhooks" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Fact]
    public async Task ManageWebhookEndpoint_ReturnsUnauthorized_WithoutOperatorCredential()
    {
        await EnsureDatabaseReadyAsync();

        using var operatorClient = CreateOperatorClient();
        var apiClient = await CreateApiClientAsync(operatorClient);
        using var client = _factory.CreateClient();

        using var registerResponse = await client.PutAsJsonAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/webhook-endpoint",
            new { url = "https://hooks.example.com/webhooks" });
        using var inspectResponse = await client.GetAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/webhook-endpoint");
        using var disableResponse = await client.PostAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/webhook-endpoint/disable",
            content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, registerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, inspectResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, disableResponse.StatusCode);
    }

    [Fact]
    public async Task Database_RejectsMoreThanOneActiveWebhookEndpointPerApiClient()
    {
        await EnsureDatabaseReadyAsync();

        using var client = CreateOperatorClient();
        var apiClient = await CreateApiClientAsync(client);
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
        var now = DateTimeOffset.UtcNow;
        dbContext.WebhookEndpoints.AddRange(
            WebhookEndpoint.Create(
                apiClient.ApiClientId,
                "https://first.example.com/webhooks",
                now),
            WebhookEndpoint.Create(
                apiClient.ApiClientId,
                "https://second.example.com/webhooks",
                now));

        var exception = await Assert.ThrowsAsync<DbUpdateException>(
            () => dbContext.SaveChangesAsync());

        var postgresException = Assert.IsType<PostgresException>(exception.InnerException);
        Assert.Equal(PostgresErrorCodes.UniqueViolation, postgresException.SqlState);
    }

    private HttpClient CreateOperatorClient()
    {
        return CreateOperatorClient(_factory);
    }

    private static WebApplicationFactory<Program> CreateFactory(
        Action<IServiceCollection>? configureServices = null)
    {
        return new WebApplicationFactory<Program>()
            .WithoutHostedServices()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(
                    "Authentication:Operator:Credential",
                    OperatorCredential);
                if (configureServices is not null)
                {
                    builder.ConfigureServices(configureServices);
                }
            });
    }

    private static HttpClient CreateOperatorClient(WebApplicationFactory<Program> factory)
    {
        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Operator", OperatorCredential);

        return client;
    }

    private static async Task<CreateApiClientResponse> CreateApiClientAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/management/api-clients",
            new { name = $"Webhook Client {Guid.NewGuid():N}" });
        var created = await response.Content.ReadFromJsonAsync<CreateApiClientResponse>();

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(created);

        return created;
    }

    private async Task EnsureDatabaseReadyAsync()
    {
        await EnsureDatabaseReadyAsync(_factory);
    }

    private static async Task EnsureDatabaseReadyAsync(
        WebApplicationFactory<Program> factory)
    {
        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
        await dbContext.Database.MigrateAsync();
    }

    private sealed record CreateApiClientResponse(
        [property: JsonPropertyName("api_client_id")] Guid ApiClientId,
        [property: JsonPropertyName("api_key")] string ApiKey);

    private sealed record RegisterWebhookEndpointResponse(
        [property: JsonPropertyName("webhook_endpoint_id")] Guid WebhookEndpointId,
        [property: JsonPropertyName("api_client_id")] Guid ApiClientId,
        [property: JsonPropertyName("url")] string Url,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("webhook_secret")] string? WebhookSecret);

    private sealed record InspectWebhookEndpointResponse(
        [property: JsonPropertyName("is_enabled")] bool IsEnabled,
        [property: JsonPropertyName("disabled_at")] DateTimeOffset? DisabledAt);

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
