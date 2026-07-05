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

public sealed class MockProviderCallbackEndpointIntegrationTests
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public MockProviderCallbackEndpointIntegrationTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithoutHostedServices();
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    [Fact]
    public async Task MockProviderCallback_MarksSentDeliveryAsDelivered()
    {
        await ResetDatabaseAsync();

        using var client = _factory.CreateClient();
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
    public async Task MockProviderCallback_MarksSentDeliveryAsFailed()
    {
        await ResetDatabaseAsync();

        using var client = _factory.CreateClient();
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

        using var client = _factory.CreateClient();
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
    public async Task MockProviderCallback_ReturnsNotFound_ForUnknownProviderMessage()
    {
        await ResetDatabaseAsync();

        using var client = _factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/provider-callbacks/mock",
            new
            {
                provider_message_id = "mock_unknown",
                status = "delivered",
            });

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

        using var client = _factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/provider-callbacks/mock",
            new
            {
                provider_message_id = "mock_message",
                status = "queued",
            });

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

        using var client = _factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/provider-callbacks/mock",
            new
            {
                provider_message_id = providerMessageId,
                status = "delivered",
            });

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
            Assert.Equal(
                1,
                await worker.ProcessBatchAsync(
                    DateTimeOffset.UtcNow,
                    CancellationToken.None));
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

    private static async Task<CallbackResult> ApplyCallbackAsync(
        HttpClient client,
        string providerMessageId,
        string status)
    {
        using var response = await client.PostAsJsonAsync(
            "/provider-callbacks/mock",
            new
            {
                provider_message_id = providerMessageId,
                status,
            });

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        return new CallbackResult(
            body.RootElement.GetProperty("delivery_id").GetGuid(),
            body.RootElement.GetProperty("provider_message_id").GetString()!,
            body.RootElement.GetProperty("status").GetString()!,
            body.RootElement.GetProperty("updated_at").GetDateTimeOffset());
    }

    private async Task ResetDatabaseAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();

        await dbContext.Database.MigrateAsync();
        await dbContext.Database.ExecuteSqlRawAsync(
            "TRUNCATE otp_challenges, delivery_attempts, deliveries, messages;");
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
}
