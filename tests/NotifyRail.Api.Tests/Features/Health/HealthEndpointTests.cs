using System.Net;
using System.Net.Http.Json;
using System.Diagnostics;
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
        Assert.Equal("unavailable", body.Status);
    }

    [Fact]
    public async Task PostgresReadinessCheck_PropagatesCancellation()
    {
        var readinessCheck = new PostgresReadinessCheck(
            "Host=127.0.0.1;Port=1;Database=notifyrail;Username=notifyrail;Password=notifyrail");
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            readinessCheck.IsReadyAsync(cancellation.Token));
    }

    [Fact]
    public async Task Readyz_LimitsReadinessCheckDuration()
    {
        var readinessCheck = new HangingReadinessCheck();
        await using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.AddSingleton<IReadinessCheck>(readinessCheck);
            });
        });
        using var client = factory.CreateClient();
        client.Timeout = TimeSpan.FromSeconds(3);

        var stopwatch = Stopwatch.StartNew();
        using var response = await client.GetAsync("/readyz");
        stopwatch.Stop();
        var body = await response.Content.ReadFromJsonAsync<ReadinessResponse>();

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
        Assert.NotNull(body);
        Assert.Equal("unavailable", body.Status);
        Assert.True(readinessCheck.WasCanceled);
        Assert.True(stopwatch.Elapsed <= TimeSpan.FromSeconds(3));
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

    private sealed class HangingReadinessCheck : IReadinessCheck
    {
        public bool WasCanceled { get; private set; }

        public async Task<bool> IsReadyAsync(CancellationToken cancellationToken)
        {
            try
            {
                await Task.Delay(Timeout.InfiniteTimeSpan, cancellationToken);
                return true;
            }
            catch (OperationCanceledException)
            {
                WasCanceled = true;
                return false;
            }
        }
    }
}
