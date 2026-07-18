using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotifyRail.Api.Features.Deliveries.Worker;
using NotifyRail.Api.Features.Messages.CreateMessage;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Tests;

public sealed class GetMessageEndpointIntegrationTests
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public GetMessageEndpointIntegrationTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory
            .WithMessageApiAuthentication()
            .WithoutHostedServices();
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    [Fact]
    public async Task GetMessage_ReturnsUnauthorized_WhenApiKeyIsMissing()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync($"/messages/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Theory]
    [InlineData("malformed")]
    [InlineData("nrk_unknown_lookup_unknown_secret")]
    public async Task GetMessage_ReturnsUnauthorizedWithoutResourceMetadata_WhenApiKeyIsInvalid(
        string apiKey)
    {
        var messageId = Guid.NewGuid();
        using var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", apiKey);

        using var response = await client.GetAsync($"/messages/{messageId}");
        var body = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.DoesNotContain(messageId.ToString(), body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task GetMessage_ReturnsMessageSummary()
    {
        await EnsureDatabaseReadyAsync();
        await ResetDatabaseAsync();

        using var client = await _factory.CreateAuthenticatedMessageClientAsync("Get Message");
        var scheduledAt = DateTimeOffset.UtcNow.AddMinutes(10);
        var receipt = await CreateMessageAsync(
            client,
            scheduledAt,
            ["+905551111111", "+905552222222"]);

        using var response = await client.GetAsync($"/messages/{receipt.MessageId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.Equal(receipt.MessageId, body.RootElement.GetProperty("message_id").GetGuid());
        Assert.Equal("transactional", body.RootElement.GetProperty("type").GetString());
        Assert.Equal("sms", body.RootElement.GetProperty("channel").GetString());
        Assert.Equal("NotifyRail", body.RootElement.GetProperty("sender_title").GetString());
        Assert.Equal("Your order is ready.", body.RootElement.GetProperty("body").GetString());
        Assert.Equal("orders", body.RootElement.GetProperty("report_label").GetString());
        Assert.Equal("unicode", body.RootElement.GetProperty("encoding").GetString());
        Assert.Equal(
            scheduledAt.ToUniversalTime().AddTicks(-(scheduledAt.ToUniversalTime().Ticks % 10)),
            body.RootElement.GetProperty("scheduled_at").GetDateTimeOffset());
        Assert.Equal(receipt.CreatedAt, body.RootElement.GetProperty("created_at").GetDateTimeOffset());
        Assert.NotEqual(
            default,
            body.RootElement.GetProperty("updated_at").GetDateTimeOffset());

        var deliveries = body.RootElement.GetProperty("deliveries");
        Assert.Equal(2, deliveries.GetProperty("total").GetInt32());
        Assert.Equal(2, deliveries.GetProperty("queued").GetInt32());
        Assert.Equal(0, deliveries.GetProperty("processing").GetInt32());
        Assert.Equal(0, deliveries.GetProperty("sent").GetInt32());
        Assert.Equal(0, deliveries.GetProperty("delivered").GetInt32());
        Assert.Equal(0, deliveries.GetProperty("retry_scheduled").GetInt32());
        Assert.Equal(0, deliveries.GetProperty("failed").GetInt32());
        Assert.Equal(0, deliveries.GetProperty("expired").GetInt32());
    }

    [Fact]
    public async Task GetMessage_ReturnsCurrentDeliveryStatusSummary()
    {
        await EnsureDatabaseReadyAsync();
        await ResetDatabaseAsync();

        using var client = await _factory.CreateAuthenticatedMessageClientAsync("Get Message");
        var receipt = await CreateMessageAsync(
            client,
            DateTimeOffset.UtcNow,
            ["+905551111111", "+905552222222"]);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var worker = scope.ServiceProvider.GetRequiredService<DeliveryWorker>();
            var processed = await worker.ProcessBatchAsync(
                DateTimeOffset.UtcNow,
                CancellationToken.None);

            Assert.Equal(1, processed);
        }

        using var response = await client.GetAsync($"/messages/{receipt.MessageId}");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        var deliveries = body.RootElement.GetProperty("deliveries");

        Assert.Equal(2, deliveries.GetProperty("total").GetInt32());
        Assert.Equal(1, deliveries.GetProperty("queued").GetInt32());
        Assert.Equal(0, deliveries.GetProperty("processing").GetInt32());
        Assert.Equal(1, deliveries.GetProperty("sent").GetInt32());
        Assert.Equal(0, deliveries.GetProperty("delivered").GetInt32());
        Assert.Equal(0, deliveries.GetProperty("retry_scheduled").GetInt32());
        Assert.Equal(0, deliveries.GetProperty("failed").GetInt32());
        Assert.Equal(0, deliveries.GetProperty("expired").GetInt32());
    }

    [Fact]
    public async Task GetMessage_ReturnsNotFound_WhenMessageDoesNotExist()
    {
        await EnsureDatabaseReadyAsync();
        await ResetDatabaseAsync();

        using var client = await _factory.CreateAuthenticatedMessageClientAsync("Get Message");
        using var response = await client.GetAsync($"/messages/{Guid.NewGuid()}");
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);

        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());
        Assert.Equal("message not found", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetMessage_ReturnsNotFound_ForAnotherApiClientsMessage()
    {
        await EnsureDatabaseReadyAsync();
        await ResetDatabaseAsync();

        using var ownerClient = await _factory.CreateAuthenticatedMessageClientAsync("Message Owner");
        using var otherClient = await _factory.CreateAuthenticatedMessageClientAsync("Other Client");
        var receipt = await CreateMessageAsync(
            ownerClient,
            DateTimeOffset.UtcNow,
            ["+905551111111"]);

        using var response = await otherClient.GetAsync($"/messages/{receipt.MessageId}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<CreateMessageResponse> CreateMessageAsync(
        HttpClient client,
        DateTimeOffset scheduledAt,
        string[] recipients)
    {
        using var response = await client.PostAsJsonAsync(
            "/messages",
            new CreateMessageRequest(
                Type: "transactional",
                Channel: "sms",
                SenderTitle: "NotifyRail",
                Body: "Your order is ready.",
                Recipients: recipients,
                IdempotencyKey: $"get-message-{Guid.NewGuid()}",
                ScheduledAt: scheduledAt,
                ReportLabel: "orders",
                Encoding: "unicode"));
        var receipt = await response.Content.ReadFromJsonAsync<CreateMessageResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return Assert.IsType<CreateMessageResponse>(receipt);
    }

    private async Task EnsureDatabaseReadyAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();

        await dbContext.Database.MigrateAsync();
    }

    private async Task ResetDatabaseAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();

        await dbContext.Database.ExecuteSqlRawAsync(
            "TRUNCATE webhook_events, otp_challenges, delivery_attempts, deliveries, messages;");
    }
}
