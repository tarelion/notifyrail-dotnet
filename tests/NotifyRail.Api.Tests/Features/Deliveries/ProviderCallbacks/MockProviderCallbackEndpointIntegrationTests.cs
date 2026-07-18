using System.Collections.Concurrent;
using System.Net;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NotifyRail.Api.Features.Deliveries.ProviderCallbacks.Mock;
using NotifyRail.Api.Features.Deliveries.Worker;
using NotifyRail.Api.Features.Messages.CreateMessage;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Tests;

public sealed class MockProviderCallbackEndpointIntegrationTests
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string CallbackSecret = "test-mock-provider-callback-secret";

    private readonly WebApplicationFactory<Program> _factory;
    private readonly MutableTimeProvider _timeProvider = new(DateTimeOffset.UtcNow);
    private readonly RecordingLoggerProvider _loggerProvider = new();

    public MockProviderCallbackEndpointIntegrationTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithMessageApiAuthentication().WithWebHostBuilder(builder =>
        {
            builder.UseSetting(
                "MockProviderCallback:Secret",
                CallbackSecret);
            builder.ConfigureLogging(logging => logging.AddProvider(_loggerProvider));
            builder.ConfigureTestServices(services =>
            {
                services.RemoveAll<IHostedService>();
                services.RemoveAll<TimeProvider>();
                services.AddSingleton<TimeProvider>(_timeProvider);
            });
        });
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    [Fact]
    public async Task MockProviderCallback_MarksSentDeliveryAsDelivered()
    {
        await ResetDatabaseAsync();

        using var client = await _factory.CreateAuthenticatedMessageClientAsync("Provider Callback");
        var sentDelivery = await CreateSentDeliveryAsync(client);

        var callback = await ApplyCallbackAsync(
            client,
            sentDelivery.ProviderMessageId,
            "delivered");

        Assert.Equal(sentDelivery.DeliveryId, callback.DeliveryId);
        Assert.Equal(sentDelivery.ProviderMessageId, callback.ProviderMessageId);
        Assert.Equal("delivered", callback.Status);
        Assert.NotEqual(default, callback.UpdatedAt);

        using var reportResponse = await client.GetAsync(
            $"/messages/{sentDelivery.MessageId}/report");
        using var report = JsonDocument.Parse(
            await reportResponse.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, reportResponse.StatusCode);
        Assert.Equal(0, report.RootElement.GetProperty("sent").GetInt32());
        Assert.Equal(1, report.RootElement.GetProperty("delivered").GetInt32());
    }

    [Fact]
    public async Task MockProviderCallback_RejectsMissingSignatureWithoutChangingDelivery()
    {
        await ResetDatabaseAsync();

        using var client = await _factory.CreateAuthenticatedMessageClientAsync("Provider Callback");
        var sentDelivery = await CreateSentDeliveryAsync(client);

        using var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/provider-callbacks/mock")
        {
            Content = JsonContent.Create(
            new
            {
                provider_message_id = sentDelivery.ProviderMessageId,
                status = "delivered",
            }),
        };

        await AssertCallbackRejectedWithoutChangingDeliveryAsync(
            client,
            sentDelivery,
            request);
    }

    [Fact]
    public async Task MockProviderCallback_RejectsTamperedBodyWithoutChangingDelivery()
    {
        await ResetDatabaseAsync();

        using var client = await _factory.CreateAuthenticatedMessageClientAsync("Provider Callback");
        var sentDelivery = await CreateSentDeliveryAsync(client);
        using var request = CreateSignedCallbackRequest(
            sentDelivery.ProviderMessageId,
            "delivered");
        request.Content = JsonContent.Create(new
        {
            provider_message_id = sentDelivery.ProviderMessageId,
            status = "failed",
        });

        await AssertCallbackRejectedWithoutChangingDeliveryAsync(
            client,
            sentDelivery,
            request);
    }

    [Theory]
    [InlineData(-6)]
    [InlineData(6)]
    public async Task MockProviderCallback_RejectsTimestampOutsideFreshnessWindow(
        int offsetMinutes)
    {
        await ResetDatabaseAsync();

        using var client = await _factory.CreateAuthenticatedMessageClientAsync("Provider Callback");
        var sentDelivery = await CreateSentDeliveryAsync(client);
        using var request = CreateSignedCallbackRequest(
            sentDelivery.ProviderMessageId,
            "delivered",
            _timeProvider.GetUtcNow().AddMinutes(offsetMinutes));

        await AssertCallbackRejectedWithoutChangingDeliveryAsync(
            client,
            sentDelivery,
            request);
    }

    [Fact]
    public async Task MockProviderCallback_RejectsSignatureFromWrongProviderSecret()
    {
        await ResetDatabaseAsync();

        using var client = await _factory.CreateAuthenticatedMessageClientAsync("Provider Callback");
        var sentDelivery = await CreateSentDeliveryAsync(client);
        using var request = CreateSignedCallbackRequest(
            sentDelivery.ProviderMessageId,
            "delivered",
            secret: "a-different-provider-secret");

        await AssertCallbackRejectedWithoutChangingDeliveryAsync(
            client,
            sentDelivery,
            request);
    }

    [Fact]
    public async Task MockProviderCallback_DoesNotExposeAuthenticationMaterial()
    {
        const string wrongSecret = "secret-that-must-not-be-exposed";
        await ResetDatabaseAsync();

        using var client = await _factory.CreateAuthenticatedMessageClientAsync("Provider Callback");
        var sentDelivery = await CreateSentDeliveryAsync(client);
        using var request = CreateSignedCallbackRequest(
            sentDelivery.ProviderMessageId,
            "delivered",
            secret: wrongSecret);
        var timestamp = Assert.Single(
            request.Headers.GetValues("X-Mock-Provider-Timestamp"));
        var signature = Assert.Single(
            request.Headers.GetValues("X-Mock-Provider-Signature"));

        using var response = await client.SendAsync(request);
        var responseText = await response.Content.ReadAsStringAsync();
        var logs = string.Join(Environment.NewLine, _loggerProvider.Messages);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(CallbackSecret, responseText, StringComparison.Ordinal);
        Assert.DoesNotContain(wrongSecret, responseText, StringComparison.Ordinal);
        Assert.DoesNotContain(timestamp, responseText, StringComparison.Ordinal);
        Assert.DoesNotContain(signature, responseText, StringComparison.Ordinal);
        Assert.DoesNotContain(CallbackSecret, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(wrongSecret, logs, StringComparison.Ordinal);
        Assert.DoesNotContain(signature, logs, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("X-Mock-Provider-Timestamp")]
    [InlineData("X-Mock-Provider-Signature")]
    public async Task MockProviderCallback_RejectsMissingAuthenticationHeader(
        string missingHeader)
    {
        await ResetDatabaseAsync();

        using var client = await _factory.CreateAuthenticatedMessageClientAsync("Provider Callback");
        var sentDelivery = await CreateSentDeliveryAsync(client);
        using var request = CreateSignedCallbackRequest(
            sentDelivery.ProviderMessageId,
            "delivered");
        request.Headers.Remove(missingHeader);

        await AssertCallbackRejectedWithoutChangingDeliveryAsync(
            client,
            sentDelivery,
            request);
    }

    [Theory]
    [InlineData("X-Mock-Provider-Timestamp", "not-a-timestamp")]
    [InlineData("X-Mock-Provider-Signature", "not-a-signature")]
    [InlineData("X-Mock-Provider-Signature", "v1=xyz")]
    public async Task MockProviderCallback_RejectsMalformedAuthenticationHeader(
        string header,
        string value)
    {
        await ResetDatabaseAsync();

        using var client = await _factory.CreateAuthenticatedMessageClientAsync("Provider Callback");
        var sentDelivery = await CreateSentDeliveryAsync(client);
        using var request = CreateSignedCallbackRequest(
            sentDelivery.ProviderMessageId,
            "delivered");
        request.Headers.Remove(header);
        request.Headers.Add(header, value);

        await AssertCallbackRejectedWithoutChangingDeliveryAsync(
            client,
            sentDelivery,
            request);
    }

    [Fact]
    public async Task MockProviderCallback_MarksSentDeliveryAsFailed()
    {
        await ResetDatabaseAsync();

        using var client = await _factory.CreateAuthenticatedMessageClientAsync("Provider Callback");
        var sentDelivery = await CreateSentDeliveryAsync(client);

        var callback = await ApplyCallbackAsync(
            client,
            sentDelivery.ProviderMessageId,
            "failed");

        Assert.Equal("failed", callback.Status);

        using var reportResponse = await client.GetAsync(
            $"/messages/{sentDelivery.MessageId}/report");
        using var report = JsonDocument.Parse(
            await reportResponse.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, reportResponse.StatusCode);
        Assert.Equal(0, report.RootElement.GetProperty("sent").GetInt32());
        Assert.Equal(1, report.RootElement.GetProperty("failed").GetInt32());
    }

    [Fact]
    public async Task MockProviderCallback_DoesNotChangeTerminalDelivery()
    {
        await ResetDatabaseAsync();

        using var client = await _factory.CreateAuthenticatedMessageClientAsync("Provider Callback");
        var sentDelivery = await CreateSentDeliveryAsync(client);

        var first = await ApplyCallbackAsync(
            client,
            sentDelivery.ProviderMessageId,
            "delivered");
        var duplicate = await ApplyCallbackAsync(
            client,
            sentDelivery.ProviderMessageId,
            "delivered");
        var conflicting = await ApplyCallbackAsync(
            client,
            sentDelivery.ProviderMessageId,
            "failed");

        Assert.Equal("delivered", first.Status);
        Assert.Equal(first, duplicate);
        Assert.Equal(first, conflicting);
    }

    [Fact]
    public async Task MockProviderCallback_RacingConflictingCallbacksPreserveFirstTerminalResult()
    {
        await ResetDatabaseAsync();

        using var client = await _factory.CreateAuthenticatedMessageClientAsync("Provider Callback");
        var sentDelivery = await CreateSentDeliveryAsync(client);
        using var deliveredRequest = CreateSignedCallbackRequest(
            sentDelivery.ProviderMessageId,
            "delivered");
        using var failedRequest = CreateSignedCallbackRequest(
            sentDelivery.ProviderMessageId,
            "failed");

        var responses = await Task.WhenAll(
            client.SendAsync(deliveredRequest),
            client.SendAsync(failedRequest));

        using var deliveredResponse = responses[0];
        using var failedResponse = responses[1];
        Assert.All(
            responses,
            response => Assert.Equal(HttpStatusCode.OK, response.StatusCode));

        var results = await Task.WhenAll(
            deliveredResponse.Content.ReadFromJsonAsync<MockProviderCallbackResponse>(),
            failedResponse.Content.ReadFromJsonAsync<MockProviderCallbackResponse>());
        Assert.All(results, Assert.NotNull);
        Assert.Equal(results[0]!.Status, results[1]!.Status);
        Assert.Contains(results[0]!.Status, new[] { "delivered", "failed" });
        Assert.Equal(results[0]!.UpdatedAt, results[1]!.UpdatedAt);
    }

    [Fact]
    public async Task MockProviderCallback_ReturnsNotFound_ForUnknownProviderMessage()
    {
        await ResetDatabaseAsync();

        using var client = await _factory.CreateAuthenticatedMessageClientAsync("Provider Callback");
        using var request = CreateSignedCallbackRequest("mock_unknown", "delivered");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.Equal(
            "provider message not found",
            body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task MockProviderCallback_ReturnsBadRequest_ForUnknownStatus()
    {
        await ResetDatabaseAsync();

        using var client = await _factory.CreateAuthenticatedMessageClientAsync("Provider Callback");
        using var request = CreateSignedCallbackRequest("mock_message", "queued");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.Equal(
            "status must be one of: delivered, failed",
            body.RootElement.GetProperty("error").GetString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public async Task MockProviderCallback_ReturnsBadRequest_ForMissingProviderMessageId(
        string? providerMessageId)
    {
        await ResetDatabaseAsync();

        using var client = await _factory.CreateAuthenticatedMessageClientAsync("Provider Callback");
        using var request = CreateSignedCallbackRequest(providerMessageId, "delivered");
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.Equal(
            "provider_message_id is required",
            body.RootElement.GetProperty("error").GetString());
    }

    private async Task<SentDelivery> CreateSentDeliveryAsync(HttpClient client)
    {
        using var createResponse = await client.PostAsJsonAsync(
            "/messages",
            new CreateMessageRequest(
                Type: "transactional",
                Channel: "sms",
                SenderTitle: "NotifyRail",
                Body: "Your order is ready.",
                Recipients: ["+905551111111"],
                IdempotencyKey: $"callback-{Guid.NewGuid()}"));
        var receipt = await createResponse.Content.ReadFromJsonAsync<CreateMessageResponse>();

        Assert.Equal(HttpStatusCode.Accepted, createResponse.StatusCode);
        Assert.NotNull(receipt);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var worker = scope.ServiceProvider.GetRequiredService<DeliveryWorker>();
            await worker.ProcessBatchAsync(
                DateTimeOffset.UtcNow,
                CancellationToken.None);
        }

        using var deliveriesResponse = await client.GetAsync(
            $"/messages/{receipt.MessageId}/deliveries");
        using var body = JsonDocument.Parse(
            await deliveriesResponse.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, deliveriesResponse.StatusCode);
        var delivery = Assert.Single(
            body.RootElement.GetProperty("deliveries").EnumerateArray());
        Assert.Equal("sent", delivery.GetProperty("status").GetString());

        return new SentDelivery(
            receipt.MessageId,
            delivery.GetProperty("delivery_id").GetGuid(),
            delivery.GetProperty("provider_message_id").GetString()!);
    }

    private async Task<CallbackResult> ApplyCallbackAsync(
        HttpClient client,
        string providerMessageId,
        string status)
    {
        using var request = CreateSignedCallbackRequest(providerMessageId, status);
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        return new CallbackResult(
            body.RootElement.GetProperty("delivery_id").GetGuid(),
            body.RootElement.GetProperty("provider_message_id").GetString()!,
            body.RootElement.GetProperty("status").GetString()!,
            body.RootElement.GetProperty("updated_at").GetDateTimeOffset());
    }

    private static async Task<string> GetDeliveryStatusAsync(
        HttpClient client,
        Guid messageId)
    {
        using var response = await client.GetAsync($"/messages/{messageId}/deliveries");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var delivery = Assert.Single(
            body.RootElement.GetProperty("deliveries").EnumerateArray());
        return delivery.GetProperty("status").GetString()!;
    }

    private static async Task AssertCallbackRejectedWithoutChangingDeliveryAsync(
        HttpClient client,
        SentDelivery sentDelivery,
        HttpRequestMessage request)
    {
        using var response = await client.SendAsync(request);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(
            "sent",
            await GetDeliveryStatusAsync(client, sentDelivery.MessageId));
    }

    private HttpRequestMessage CreateSignedCallbackRequest(
        string? providerMessageId,
        string status,
        DateTimeOffset? signedAt = null,
        string secret = CallbackSecret)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            provider_message_id = providerMessageId,
            status,
        });
        var timestamp = (signedAt ?? _timeProvider.GetUtcNow())
            .ToUnixTimeSeconds()
            .ToString();
        var signedPayload = Encoding.UTF8.GetBytes($"{timestamp}.")
            .Concat(body)
            .ToArray();
        var signature = Convert.ToHexStringLower(
            HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(secret),
                signedPayload));

        var request = new HttpRequestMessage(
            HttpMethod.Post,
            "/provider-callbacks/mock")
        {
            Content = new ByteArrayContent(body),
        };
        request.Content.Headers.ContentType =
            new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");
        request.Headers.Add("X-Mock-Provider-Timestamp", timestamp);
        request.Headers.Add("X-Mock-Provider-Signature", $"v1={signature}");
        return request;
    }

    private async Task ResetDatabaseAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();

        await dbContext.Database.MigrateAsync();
        await dbContext.Database.ExecuteSqlRawAsync(
            "TRUNCATE webhook_events, otp_challenges, delivery_attempts, deliveries, messages;");
    }

    private sealed record SentDelivery(
        Guid MessageId,
        Guid DeliveryId,
        string ProviderMessageId);

    private sealed record CallbackResult(
        Guid DeliveryId,
        string ProviderMessageId,
        string Status,
        DateTimeOffset UpdatedAt);

    private sealed class MutableTimeProvider(DateTimeOffset utcNow) : TimeProvider
    {
        public override DateTimeOffset GetUtcNow() => utcNow;
    }

    private sealed class RecordingLoggerProvider : ILoggerProvider
    {
        public ConcurrentQueue<string> Messages { get; } = new();

        public ILogger CreateLogger(string categoryName) =>
            new RecordingLogger(Messages);

        public void Dispose()
        {
        }

        private sealed class RecordingLogger(
            ConcurrentQueue<string> messages) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                messages.Enqueue(formatter(state, exception));
            }
        }
    }
}
