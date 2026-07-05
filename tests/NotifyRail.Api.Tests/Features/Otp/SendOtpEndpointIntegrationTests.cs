using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Tests;

public sealed class SendOtpEndpointIntegrationTests
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public SendOtpEndpointIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithoutHostedServices();
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    [Fact]
    public async Task SendOtp_CreatesChallengeAndRecipientDelivery()
    {
        await ResetDatabaseAsync();

        using var client = _factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/otp/send",
            new
            {
                recipient = "+905551111111",
                idempotency_key = $"otp-send-{Guid.NewGuid()}",
            });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        var otpId = body.RootElement.GetProperty("otp_id").GetGuid();
        var messageId = body.RootElement.GetProperty("message_id").GetGuid();
        var expiresAt = body.RootElement.GetProperty("expires_at").GetDateTimeOffset();
        var debugCode = body.RootElement.GetProperty("debug_code").GetString();

        Assert.NotEqual(Guid.Empty, otpId);
        Assert.NotEqual(Guid.Empty, messageId);
        Assert.True(expiresAt > DateTimeOffset.UtcNow);
        Assert.Matches("^[0-9]{6}$", debugCode);

        using var deliveriesResponse = await client.GetAsync(
            $"/messages/{messageId}/deliveries");
        using var deliveriesBody = JsonDocument.Parse(
            await deliveriesResponse.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, deliveriesResponse.StatusCode);
        var delivery = Assert.Single(
            deliveriesBody.RootElement.GetProperty("deliveries").EnumerateArray());
        Assert.Equal("+905551111111", delivery.GetProperty("recipient").GetString());
        Assert.Equal("queued", delivery.GetProperty("status").GetString());
        Assert.Equal(expiresAt, delivery.GetProperty("expires_at").GetDateTimeOffset());
    }

    [Fact]
    public async Task SendOtp_ReplaysSameResponse_ForSameIdempotencyKey()
    {
        await ResetDatabaseAsync();

        using var client = _factory.CreateClient();
        var idempotencyKey = $"otp-replay-{Guid.NewGuid()}";
        var request = new
        {
            recipient = " +905551111111 ",
            idempotency_key = $" {idempotencyKey} ",
        };

        using var firstResponse = await client.PostAsJsonAsync("/otp/send", request);
        using var replayResponse = await client.PostAsJsonAsync(
            "/otp/send",
            new
            {
                recipient = "+905551111111",
                idempotency_key = idempotencyKey,
            });

        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, replayResponse.StatusCode);
        Assert.Equal(
            await firstResponse.Content.ReadAsStringAsync(),
            await replayResponse.Content.ReadAsStringAsync());
    }

    [Fact]
    public async Task SendOtp_PersistsOnlyHashedCode()
    {
        await ResetDatabaseAsync();

        using var client = _factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/otp/send",
            new
            {
                recipient = "+905551111111",
                idempotency_key = $"otp-hash-{Guid.NewGuid()}",
            });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var otpId = body.RootElement.GetProperty("otp_id").GetGuid();
        var debugCode = body.RootElement.GetProperty("debug_code").GetString()!;

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
        var persisted = await dbContext.OtpChallenges
            .AsNoTracking()
            .Where(challenge => challenge.Id == otpId)
            .Select(challenge => new
            {
                challenge.CodeHash,
                challenge.Message.Body,
            })
            .SingleAsync();

        Assert.Equal(32, persisted.CodeHash.Length);
        Assert.NotEqual(Encoding.UTF8.GetBytes(debugCode), persisted.CodeHash);
        Assert.DoesNotContain(debugCode, persisted.Body, StringComparison.Ordinal);
    }

    [Fact]
    public async Task SendOtp_ReplaysSameResponse_ForConcurrentSameKeyRequests()
    {
        await ResetDatabaseAsync();

        using var client = _factory.CreateClient();
        var request = new
        {
            recipient = "+905551111111",
            idempotency_key = $"otp-concurrent-{Guid.NewGuid()}",
        };

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 8)
                .Select(_ => client.PostAsJsonAsync("/otp/send", request)));

        try
        {
            Assert.All(
                responses,
                response => Assert.Equal(HttpStatusCode.Accepted, response.StatusCode));
            var bodies = await Task.WhenAll(
                responses.Select(response => response.Content.ReadAsStringAsync()));
            Assert.All(bodies, body => Assert.Equal(bodies[0], body));
        }
        finally
        {
            foreach (var response in responses)
            {
                response.Dispose();
            }
        }
    }

    [Fact]
    public async Task SendOtp_ReturnsConflict_WhenIdempotencyKeyContentChanges()
    {
        await ResetDatabaseAsync();

        using var client = _factory.CreateClient();
        var idempotencyKey = $"otp-conflict-{Guid.NewGuid()}";
        using var first = await client.PostAsJsonAsync(
            "/otp/send",
            new
            {
                recipient = "+905551111111",
                idempotency_key = idempotencyKey,
            });
        using var conflict = await client.PostAsJsonAsync(
            "/otp/send",
            new
            {
                recipient = "+905552222222",
                idempotency_key = idempotencyKey,
            });

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, conflict.StatusCode);
    }

    private async Task ResetDatabaseAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();

        await dbContext.Database.MigrateAsync();
        await dbContext.Database.ExecuteSqlRawAsync(
            "TRUNCATE otp_challenges, delivery_attempts, deliveries, messages;");
    }
}
