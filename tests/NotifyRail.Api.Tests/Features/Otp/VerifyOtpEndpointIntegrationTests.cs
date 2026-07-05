using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Tests;

public sealed class VerifyOtpEndpointIntegrationTests
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public VerifyOtpEndpointIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithoutHostedServices();
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    [Fact]
    public async Task VerifyOtp_VerifiesActiveChallengeWithCorrectCode()
    {
        await ResetDatabaseAsync();

        using var client = _factory.CreateClient();
        var challenge = await SendOtpAsync(client);

        using var response = await client.PostAsJsonAsync(
            "/otp/verify",
            new
            {
                otp_id = challenge.OtpId,
                code = challenge.DebugCode,
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.Equal(challenge.OtpId, body.RootElement.GetProperty("otp_id").GetGuid());
        Assert.Equal("verified", body.RootElement.GetProperty("status").GetString());
        Assert.NotEqual(
            default,
            body.RootElement.GetProperty("verified_at").GetDateTimeOffset());
    }

    [Fact]
    public async Task VerifyOtp_RejectsCodeAfterSuccessfulVerification()
    {
        await ResetDatabaseAsync();

        using var client = _factory.CreateClient();
        var challenge = await SendOtpAsync(client);
        var request = new
        {
            otp_id = challenge.OtpId,
            code = challenge.DebugCode,
        };

        using var first = await client.PostAsJsonAsync("/otp/verify", request);
        using var second = await client.PostAsJsonAsync("/otp/verify", request);

        Assert.Equal(HttpStatusCode.OK, first.StatusCode);
        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
        using var error = JsonDocument.Parse(await second.Content.ReadAsStreamAsync());
        Assert.Equal(
            "OTP challenge is already verified",
            error.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task VerifyOtp_AllowsOnlyOneConcurrentSuccessfulVerification()
    {
        await ResetDatabaseAsync();

        using var client = _factory.CreateClient();
        var challenge = await SendOtpAsync(client);
        var request = new
        {
            otp_id = challenge.OtpId,
            code = challenge.DebugCode,
        };

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 8)
                .Select(_ => client.PostAsJsonAsync("/otp/verify", request)));

        try
        {
            Assert.Single(responses, response => response.StatusCode == HttpStatusCode.OK);
            Assert.Equal(
                7,
                responses.Count(response => response.StatusCode == HttpStatusCode.Conflict));
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
    public async Task VerifyOtp_LocksChallengeAfterMaximumIncorrectAttempts()
    {
        await ResetDatabaseAsync();

        using var client = _factory.CreateClient();
        var challenge = await SendOtpAsync(client);
        var incorrectCode = challenge.DebugCode == "000000" ? "000001" : "000000";

        for (var attempt = 1; attempt <= 4; attempt++)
        {
            using var response = await VerifyAsync(client, challenge.OtpId, incorrectCode);
            using var error = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal("invalid OTP code", error.RootElement.GetProperty("error").GetString());
            Assert.Equal(
                5 - attempt,
                error.RootElement.GetProperty("attempts_remaining").GetInt32());
        }

        using var finalIncorrect = await VerifyAsync(
            client,
            challenge.OtpId,
            incorrectCode);
        using var lockedCorrect = await VerifyAsync(
            client,
            challenge.OtpId,
            challenge.DebugCode);

        Assert.Equal(HttpStatusCode.TooManyRequests, finalIncorrect.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, lockedCorrect.StatusCode);
    }

    [Fact]
    public async Task VerifyOtp_RejectsCorrectCodeAfterChallengeExpires()
    {
        var timeProvider = new MutableTimeProvider(
            new DateTimeOffset(2026, 7, 5, 12, 0, 0, TimeSpan.Zero));
        await using var factory = _factory.WithWebHostBuilder(builder =>
        {
            builder.ConfigureServices(services =>
            {
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(timeProvider);
            });
        });

        await ResetDatabaseAsync(factory.Services);

        using var client = factory.CreateClient();
        var challenge = await SendOtpAsync(client);
        timeProvider.Advance(TimeSpan.FromMinutes(6));

        using var response = await VerifyAsync(
            client,
            challenge.OtpId,
            challenge.DebugCode);

        Assert.Equal(HttpStatusCode.Gone, response.StatusCode);
        using var error = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.Equal(
            "OTP challenge has expired",
            error.RootElement.GetProperty("error").GetString());
    }

    private static async Task<SentChallenge> SendOtpAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/otp/send",
            new
            {
                recipient = "+905551111111",
                idempotency_key = $"otp-verify-{Guid.NewGuid()}",
            });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        return new SentChallenge(
            body.RootElement.GetProperty("otp_id").GetGuid(),
            body.RootElement.GetProperty("debug_code").GetString()!);
    }

    private static Task<HttpResponseMessage> VerifyAsync(
        HttpClient client,
        Guid otpId,
        string code)
    {
        return client.PostAsJsonAsync(
            "/otp/verify",
            new
            {
                otp_id = otpId,
                code,
            });
    }

    private async Task ResetDatabaseAsync()
    {
        await ResetDatabaseAsync(_factory.Services);
    }

    private static async Task ResetDatabaseAsync(IServiceProvider services)
    {
        await using var scope = services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();

        await dbContext.Database.MigrateAsync();
        await dbContext.Database.ExecuteSqlRawAsync(
            "TRUNCATE otp_challenges, delivery_attempts, deliveries, messages;");
    }

    private sealed record SentChallenge(Guid OtpId, string DebugCode);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        private DateTimeOffset _utcNow = utcNow;

        public override DateTimeOffset GetUtcNow()
        {
            return _utcNow;
        }

        public void Advance(TimeSpan duration)
        {
            _utcNow = _utcNow.Add(duration);
        }
    }
}
