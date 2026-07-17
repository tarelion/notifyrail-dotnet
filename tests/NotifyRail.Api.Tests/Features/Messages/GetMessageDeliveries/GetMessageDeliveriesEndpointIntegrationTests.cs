using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotifyRail.Api.Features.Deliveries.Worker;
using NotifyRail.Api.Features.Messages.CreateMessage;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Tests;

public sealed class GetMessageDeliveriesEndpointIntegrationTests
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public GetMessageDeliveriesEndpointIntegrationTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory
            .WithoutHostedServices()
            .WithMessageApiAuthentication();
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    [Fact]
    public async Task GetMessageDeliveries_ReturnsUnauthorized_WhenApiKeyIsMissing()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync($"/messages/{Guid.NewGuid()}/deliveries");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMessageDeliveries_ReturnsRecipientDeliveries()
    {
        await EnsureDatabaseReadyAsync();
        await ResetDatabaseAsync();

        using var client = await _factory.CreateAuthenticatedMessageClientAsync("Get Deliveries");
        var receipt = await CreateMessageAsync(
            client,
            ["+905551111111", "+905552222222"]);

        using var response = await client.GetAsync($"/messages/{receipt.MessageId}/deliveries");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(receipt.MessageId, body.RootElement.GetProperty("message_id").GetGuid());

        var deliveries = body.RootElement.GetProperty("deliveries").EnumerateArray().ToArray();
        Assert.Equal(2, deliveries.Length);
        Assert.Equal(
            ["+905551111111", "+905552222222"],
            deliveries
                .Select(delivery => delivery.GetProperty("recipient").GetString()!)
                .Order()
                .ToArray());
        Assert.All(deliveries, delivery =>
        {
            Assert.NotEqual(Guid.Empty, delivery.GetProperty("delivery_id").GetGuid());
            Assert.Equal("queued", delivery.GetProperty("status").GetString());
            Assert.Equal(0, delivery.GetProperty("attempt_count").GetInt32());
            Assert.Equal(JsonValueKind.Null, delivery.GetProperty("next_attempt_at").ValueKind);
            Assert.Equal(JsonValueKind.Null, delivery.GetProperty("provider_message_id").ValueKind);
            Assert.Empty(delivery.GetProperty("attempts").EnumerateArray());
        });
    }

    [Fact]
    public async Task GetMessageDeliveries_ReturnsDeliveryAttemptHistory()
    {
        await EnsureDatabaseReadyAsync();
        await ResetDatabaseAsync();

        using var client = await _factory.CreateAuthenticatedMessageClientAsync("Get Deliveries");
        var receipt = await CreateMessageAsync(client, ["+905551111111"]);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var worker = scope.ServiceProvider.GetRequiredService<DeliveryWorker>();
            var processed = await worker.ProcessBatchAsync(
                DateTimeOffset.UtcNow,
                CancellationToken.None);

            Assert.Equal(1, processed);
        }

        using var response = await client.GetAsync($"/messages/{receipt.MessageId}/deliveries");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var delivery = Assert.Single(
            body.RootElement.GetProperty("deliveries").EnumerateArray());
        Assert.Equal("sent", delivery.GetProperty("status").GetString());
        Assert.Equal(1, delivery.GetProperty("attempt_count").GetInt32());

        var providerMessageId = delivery.GetProperty("provider_message_id").GetString();
        Assert.NotNull(providerMessageId);

        var attempt = Assert.Single(delivery.GetProperty("attempts").EnumerateArray());
        Assert.Equal(1, attempt.GetProperty("attempt_number").GetInt32());
        Assert.Equal("mock", attempt.GetProperty("provider").GetString());
        Assert.Equal("accepted", attempt.GetProperty("outcome").GetString());
        Assert.Equal(
            providerMessageId,
            attempt.GetProperty("provider_message_id").GetString());
        Assert.Equal(JsonValueKind.Null, attempt.GetProperty("error_code").ValueKind);
        Assert.Equal(JsonValueKind.Null, attempt.GetProperty("error_message").ValueKind);
        Assert.NotEqual(
            default,
            attempt.GetProperty("attempted_at").GetDateTimeOffset());
    }

    [Fact]
    public async Task GetMessageDeliveries_ReturnsNotFound_WhenMessageDoesNotExist()
    {
        await EnsureDatabaseReadyAsync();
        await ResetDatabaseAsync();

        using var client = await _factory.CreateAuthenticatedMessageClientAsync("Get Deliveries");
        using var response = await client.GetAsync($"/messages/{Guid.NewGuid()}/deliveries");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("message not found", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetMessageDeliveries_ReturnsNotFound_ForAnotherApiClientsMessage()
    {
        await EnsureDatabaseReadyAsync();
        await ResetDatabaseAsync();

        using var ownerClient = await _factory.CreateAuthenticatedMessageClientAsync("Message Owner");
        using var otherClient = await _factory.CreateAuthenticatedMessageClientAsync("Other Client");
        var receipt = await CreateMessageAsync(ownerClient, ["+905551111111"]);

        using var response = await otherClient.GetAsync(
            $"/messages/{receipt.MessageId}/deliveries");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<CreateMessageResponse> CreateMessageAsync(
        HttpClient client,
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
                IdempotencyKey: $"get-deliveries-{Guid.NewGuid()}"));
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
            "TRUNCATE otp_challenges, delivery_attempts, deliveries, messages;");
    }
}
