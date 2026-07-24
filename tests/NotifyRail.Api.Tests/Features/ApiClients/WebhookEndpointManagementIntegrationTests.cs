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
using NotifyRail.Api.Features.Webhooks;
using NotifyRail.Api.Features.Webhooks.Dispatch;
using NotifyRail.Api.Features.Webhooks.Queue;
using NotifyRail.Api.Infrastructure.Persistence;
using NotifyRail.Api.Telemetry;
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
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(
                    "Authentication:Operator:Credential",
                    OperatorCredential);
                builder.UseSetting("Webhooks:AllowLocalhostEndpoints", "false");
                builder.ConfigureServices(UsePublicDnsAnswer);
            });
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
    public async Task RotateWebhookSecret_ReturnsNewSecretOnceAndExposesOnlyRotationMetadata()
    {
        var now = new DateTimeOffset(2026, 7, 20, 12, 0, 0, TimeSpan.Zero);
        using var factory = CreateFactory(services =>
        {
            services.RemoveAll<TimeProvider>();
            services.AddSingleton<TimeProvider>(new FixedTimeProvider(now));
        });
        await EnsureDatabaseReadyAsync(factory);

        using var client = CreateOperatorClient(factory);
        var apiClient = await CreateApiClientAsync(client);
        var endpointRoute =
            $"/management/api-clients/{apiClient.ApiClientId}/webhook-endpoint";
        using var registerResponse = await client.PutAsJsonAsync(
            endpointRoute,
            new { url = "https://hooks.example.com/notifyrail" });
        var registered = await registerResponse.Content
            .ReadFromJsonAsync<RegisterWebhookEndpointResponse>();
        Assert.NotNull(registered?.WebhookSecret);

        using var rotationResponse = await client.PostAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/webhook-secret/rotate",
            content: null);
        var rotated = await rotationResponse.Content
            .ReadFromJsonAsync<RotateWebhookSecretResponse>();

        Assert.Equal(HttpStatusCode.Created, rotationResponse.StatusCode);
        Assert.NotNull(rotated);
        Assert.StartsWith("nrs_", rotated.WebhookSecret, StringComparison.Ordinal);
        Assert.NotEqual(registered.WebhookSecret, rotated.WebhookSecret);
        Assert.Equal(now, rotated.CreatedAt);
        Assert.Equal(now.AddHours(24), rotated.OverlapExpiresAt);

        using var inspectResponse = await client.GetAsync(endpointRoute);
        using var inspected = JsonDocument.Parse(
            await inspectResponse.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, inspectResponse.StatusCode);
        Assert.False(inspected.RootElement.TryGetProperty("webhook_secret", out _));
        Assert.Equal(
            now,
            inspected.RootElement.GetProperty("webhook_secret_created_at")
                .GetDateTimeOffset());
        Assert.Equal(
            now.AddHours(24),
            inspected.RootElement.GetProperty("webhook_secret_overlap_expires_at")
                .GetDateTimeOffset());

        await using var scope = factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
        var protector = scope.ServiceProvider.GetRequiredService<IWebhookSecretProtector>();
        var secrets = await dbContext.WebhookSecrets
            .AsNoTracking()
            .Where(secret => secret.ApiClientId == apiClient.ApiClientId)
            .OrderBy(secret => secret.RetiredAt == null ? 1 : 0)
            .ToListAsync();

        Assert.Equal(2, secrets.Count);
        Assert.All(secrets, secret => Assert.NotEmpty(secret.ProtectedValue));
        Assert.Equal(registered.WebhookSecret, protector.Unprotect(secrets[0].ProtectedValue));
        Assert.Equal(rotated.WebhookSecret, protector.Unprotect(secrets[1].ProtectedValue));
        Assert.Equal(now.AddHours(24), secrets[0].RetiredAt);
        Assert.Null(secrets[1].RetiredAt);
        Assert.DoesNotContain(
            registered.WebhookSecret,
            Encoding.UTF8.GetString(secrets[0].ProtectedValue),
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            rotated.WebhookSecret,
            Encoding.UTF8.GetString(secrets[1].ProtectedValue),
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task RotateWebhookSecret_ReturnsNotFound_WhenInitialSecretDoesNotExist()
    {
        await EnsureDatabaseReadyAsync();

        using var client = CreateOperatorClient();
        var apiClient = await CreateApiClientAsync(client);
        using var response = await client.PostAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/webhook-secret/rotate",
            content: null);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task InspectWebhookEndpoint_WaitsForRotationAndReturnsOneMetadataSnapshot()
    {
        await EnsureDatabaseReadyAsync();

        using var client = CreateOperatorClient();
        var apiClient = await CreateApiClientAsync(client);
        var route = $"/management/api-clients/{apiClient.ApiClientId}/webhook-endpoint";
        using var registration = await client.PutAsJsonAsync(
            route,
            new { url = "https://metadata.example.com/webhooks" });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        var rotatedAt = DateTimeOffset.UtcNow.AddMinutes(1);
        rotatedAt = rotatedAt.AddTicks(-(rotatedAt.Ticks % 10));
        var overlapExpiresAt = rotatedAt.AddHours(24);
        await using var rotationScope = _factory.Services.CreateAsyncScope();
        var rotationContext = rotationScope.ServiceProvider
            .GetRequiredService<NotifyRailDbContext>();
        var protector = rotationScope.ServiceProvider
            .GetRequiredService<IWebhookSecretProtector>();
        await using var rotationTransaction =
            await rotationContext.Database.BeginTransactionAsync();
        await rotationContext.ApiClients
            .FromSqlInterpolated(
                $"SELECT * FROM api_clients WHERE id = {apiClient.ApiClientId} FOR UPDATE")
            .SingleAsync();
        var current = await rotationContext.WebhookSecrets.SingleAsync(
            secret => secret.ApiClientId == apiClient.ApiClientId && secret.RetiredAt == null);
        current.Retire(overlapExpiresAt);
        rotationContext.WebhookSecrets.Add(WebhookSecret.Create(
            apiClient.ApiClientId,
            protector.Protect("nrs_metadata-snapshot-secret"),
            rotatedAt));
        await rotationContext.SaveChangesAsync();

        var inspecting = client.GetAsync(route);
        await Task.Delay(TimeSpan.FromMilliseconds(150));
        Assert.False(inspecting.IsCompleted);

        await rotationTransaction.CommitAsync();
        using var response = await inspecting.WaitAsync(TimeSpan.FromSeconds(3));
        using var inspected = JsonDocument.Parse(await response.Content.ReadAsStringAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(
            rotatedAt,
            inspected.RootElement.GetProperty("webhook_secret_created_at")
                .GetDateTimeOffset());
        Assert.Equal(
            overlapExpiresAt,
            inspected.RootElement.GetProperty("webhook_secret_overlap_expires_at")
                .GetDateTimeOffset());
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
    public async Task RegisterWebhookEndpoint_RejectsDnsNameResolvingToLoopback_WhenLocalhostIsEnabled()
    {
        using var factory = CreateFactory(services =>
        {
            services.RemoveAll<IWebhookDnsResolver>();
            services.AddSingleton<IWebhookDnsResolver>(new StubWebhookDnsResolver(
                IPAddress.Loopback));
        }, allowLocalhostEndpoints: true);
        await EnsureDatabaseReadyAsync(factory);

        using var client = CreateOperatorClient(factory);
        var apiClient = await CreateApiClientAsync(client);
        using var response = await client.PutAsJsonAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/webhook-endpoint",
            new { url = "https://not-localhost.example/webhooks" });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterWebhookEndpoint_AllowsPublicIpv6Address()
    {
        await EnsureDatabaseReadyAsync();

        using var client = CreateOperatorClient();
        var apiClient = await CreateApiClientAsync(client);
        using var response = await client.PutAsJsonAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/webhook-endpoint",
            new { url = "https://[2606:4700:4700::1111]/webhooks" });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    }

    [Theory]
    [InlineData("https://0.0.0.0/webhooks")]
    [InlineData("https://10.20.30.40/webhooks")]
    [InlineData("https://169.254.169.254/latest/meta-data")]
    [InlineData("https://172.16.0.1/webhooks")]
    [InlineData("https://192.168.1.1/webhooks")]
    [InlineData("https://192.88.99.2/webhooks")]
    [InlineData("https://224.0.0.1/webhooks")]
    [InlineData("https://[::]/webhooks")]
    [InlineData("https://[fc00::1]/webhooks")]
    [InlineData("https://[fe80::1]/webhooks")]
    [InlineData("https://[ff02::1]/webhooks")]
    [InlineData("https://[::ffff:10.0.0.1]/webhooks")]
    [InlineData("https://[64:ff9b:1::1]/webhooks")]
    [InlineData("https://[2001:2::1]/webhooks")]
    [InlineData("https://[2001:5::1]/webhooks")]
    [InlineData("https://[2001:db8::1]/webhooks")]
    [InlineData("https://[2002:a00:1::]/webhooks")]
    [InlineData("https://[3fff::1]/webhooks")]
    [InlineData("https://[5f00::1]/webhooks")]
    public async Task RegisterWebhookEndpoint_RejectsNonPublicAddress(string url)
    {
        await EnsureDatabaseReadyAsync();

        using var client = CreateOperatorClient();
        var apiClient = await CreateApiClientAsync(client);
        using var response = await client.PutAsJsonAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/webhook-endpoint",
            new { url });
        var error = await response.Content
            .ReadFromJsonAsync<RegisterWebhookEndpointErrorResponse>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal("url host must resolve only to public addresses", error.Error);
        Assert.DoesNotContain(new Uri(url).Host, error.Error, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task RegisterWebhookEndpoint_RejectsMixedPublicAndPrivateDnsAnswers()
    {
        using var factory = CreateFactory(services =>
        {
            services.RemoveAll<IWebhookDnsResolver>();
            services.AddSingleton<IWebhookDnsResolver>(new StubWebhookDnsResolver(
                IPAddress.Parse("93.184.216.34"),
                IPAddress.Parse("10.0.0.8")));
        });
        await EnsureDatabaseReadyAsync(factory);

        using var client = CreateOperatorClient(factory);
        var apiClient = await CreateApiClientAsync(client);
        const string url = "https://mixed-answers.example/webhooks?token=secret";
        using var response = await client.PutAsJsonAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/webhook-endpoint",
            new { url });
        var error = await response.Content
            .ReadFromJsonAsync<RegisterWebhookEndpointErrorResponse>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal("url host must resolve only to public addresses", error.Error);
        Assert.DoesNotContain("mixed-answers", error.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", error.Error, StringComparison.Ordinal);
        Assert.DoesNotContain("10.0.0.8", error.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task WebhookDispatcher_RejectsDnsRebindingAfterEndpointRegistration()
    {
        using var factory = CreateFactory(services =>
        {
            services.RemoveAll<IWebhookDnsResolver>();
            services.AddSingleton<IWebhookDnsResolver>(new SequenceWebhookDnsResolver(
                [IPAddress.Parse("93.184.216.34")],
                [IPAddress.Parse("93.184.216.34"), IPAddress.Parse("10.0.0.8")]));
        });
        await EnsureDatabaseReadyAsync(factory);

        using var client = CreateOperatorClient(factory);
        var apiClient = await CreateApiClientAsync(client);
        const string url = "https://rebind.example/webhooks?token=secret";
        using var registration = await client.PutAsJsonAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/webhook-endpoint",
            new { url });
        Assert.Equal(HttpStatusCode.Created, registration.StatusCode);

        await using var scope = factory.Services.CreateAsyncScope();
        var protector = scope.ServiceProvider.GetRequiredService<IWebhookSecretProtector>();
        var dispatcher = scope.ServiceProvider.GetRequiredService<WebhookDispatcher>();
        var result = await dispatcher.SendAsync(
            new WebhookRequest(
                Guid.NewGuid(),
                new TelemetryCorrelation(
                    apiClient.ApiClientId,
                    Guid.NewGuid(),
                    Guid.NewGuid(),
                    SourceTraceParent: null),
                url,
                "{}",
                protector.Protect("nrs_test-secret")),
            DateTimeOffset.UtcNow,
            CancellationToken.None);

        Assert.Equal(WebhookOutcome.PermanentFailure, result.Outcome);
        Assert.Equal("unsafe_endpoint", result.ErrorCode);
        Assert.Equal(
            "Webhook endpoint resolved to a prohibited network destination.",
            result.ErrorMessage);
        Assert.DoesNotContain("rebind", result.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("secret", result.ErrorMessage, StringComparison.Ordinal);
        Assert.DoesNotContain("10.0.0.8", result.ErrorMessage, StringComparison.Ordinal);
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
        using var rotateResponse = await client.PostAsync(
            $"/management/api-clients/{apiClient.ApiClientId}/webhook-secret/rotate",
            content: null);

        Assert.Equal(HttpStatusCode.Unauthorized, registerResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, inspectResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, disableResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Unauthorized, rotateResponse.StatusCode);
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
        Action<IServiceCollection>? configureServices = null,
        bool allowLocalhostEndpoints = false)
    {
        return new WebApplicationFactory<Program>()
            .WithoutHostedServices()
            .WithWebHostBuilder(builder =>
            {
                builder.UseSetting(
                    "Authentication:Operator:Credential",
                    OperatorCredential);
                builder.UseSetting(
                    "Webhooks:AllowLocalhostEndpoints",
                    allowLocalhostEndpoints.ToString());
                builder.ConfigureServices(UsePublicDnsAnswer);
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

    private sealed record RotateWebhookSecretResponse(
        [property: JsonPropertyName("webhook_secret")] string WebhookSecret,
        [property: JsonPropertyName("created_at")] DateTimeOffset CreatedAt,
        [property: JsonPropertyName("overlap_expires_at")]
        DateTimeOffset OverlapExpiresAt);

    private sealed record RegisterWebhookEndpointErrorResponse(
        [property: JsonPropertyName("error")] string Error);

    private sealed class StubWebhookDnsResolver(params IPAddress[] addresses)
        : IWebhookDnsResolver
    {
        public ValueTask<IPAddress[]> ResolveAsync(
            string host,
            CancellationToken cancellationToken)
        {
            return ValueTask.FromResult(addresses);
        }
    }

    private sealed class SequenceWebhookDnsResolver(params IPAddress[][] answers)
        : IWebhookDnsResolver
    {
        private int _index = -1;

        public ValueTask<IPAddress[]> ResolveAsync(
            string host,
            CancellationToken cancellationToken)
        {
            var index = Math.Min(Interlocked.Increment(ref _index), answers.Length - 1);
            return ValueTask.FromResult(answers[index]);
        }
    }

    private static void UsePublicDnsAnswer(IServiceCollection services)
    {
        services.RemoveAll<IWebhookDnsResolver>();
        services.AddSingleton<IWebhookDnsResolver>(new StubWebhookDnsResolver(
            IPAddress.Parse("93.184.216.34")));
    }

    private sealed class FixedTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }
}
