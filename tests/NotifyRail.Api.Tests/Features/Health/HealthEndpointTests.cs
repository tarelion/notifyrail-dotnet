using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.DependencyInjection;
using NotifyRail.Api.Features.Health;

namespace NotifyRail.Api.Tests;

public sealed class HealthEndpointTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthEndpointTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithoutHostedServices();
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    [Fact]
    public async Task Healthz_ReturnsOk()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync("/healthz");
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("ok", body.Status);
    }

    [Fact]
    public async Task Readyz_ReturnsOk_WhenDatabaseIsReachable()
    {
        await using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IReadinessCheck>(new StubReadinessCheck(true));
            });
        });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/readyz");
        var body = await response.Content.ReadFromJsonAsync<ReadinessResponse>();

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("ready", body.Status);
    }

    [Fact]
    public async Task Readyz_ReturnsServiceUnavailable_WhenDatabaseIsNotReachable()
    {
        await using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IReadinessCheck>(new StubReadinessCheck(false));
            });
        });
        using var client = factory.CreateClient();

        using var response = await client.GetAsync("/readyz");
        var body = await response.Content.ReadFromJsonAsync<ReadinessResponse>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("not_ready", body.Status);
    }

    private sealed class StubReadinessCheck : IReadinessCheck
    {
        private readonly bool _isReady;

        public StubReadinessCheck(bool isReady)
        {
            _isReady = isReady;
        }

        public Task<bool> IsReadyAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult(_isReady);
        }
    }
}
