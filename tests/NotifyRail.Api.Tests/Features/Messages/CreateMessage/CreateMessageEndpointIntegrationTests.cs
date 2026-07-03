using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotifyRail.Api.Features.Messages.CreateMessage;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Tests;

public sealed class CreateMessageEndpointIntegrationTests
    : IClassFixture<WebApplicationFactory<Program>>
{
    private const string FailingRecipient = "__notifyrail_fail_delivery_insert__";

    private readonly WebApplicationFactory<Program> _factory;

    public CreateMessageEndpointIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task CreateMessage_ReplaysSameResponse_ForConcurrentSameKeyRequests()
    {
        await EnsureDatabaseReadyAsync();

        using var client = _factory.CreateClient();
        var request = ValidRequest($"concurrent-{Guid.NewGuid()}");

        var responses = await Task.WhenAll(
            Enumerable.Range(0, 8)
                .Select(_ => client.PostAsJsonAsync("/messages", request)));

        var receipts = new List<CreateMessageResponse>();
        foreach (var response in responses)
        {
            Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

            var receipt = await response.Content.ReadFromJsonAsync<CreateMessageResponse>();
            Assert.NotNull(receipt);
            receipts.Add(receipt);
        }

        var first = receipts[0];
        Assert.All(receipts, receipt =>
        {
            Assert.Equal(first.MessageId, receipt.MessageId);
            Assert.Equal(first.DeliveryCount, receipt.DeliveryCount);
            Assert.Equal(first.CreatedAt, receipt.CreatedAt);
        });
    }

    [Fact]
    public async Task CreateMessage_RollsBackPartialWrite_WhenDeliveryInsertFails()
    {
        await EnsureDatabaseReadyAsync();
        await InstallFailingDeliveryTriggerAsync();

        using var client = _factory.CreateClient();
        var request = ValidRequest(
            $"partial-write-{Guid.NewGuid()}",
            recipients: ["+905551111111", FailingRecipient]);

        try
        {
            await AssertRequestFailsAsync(client.PostAsJsonAsync("/messages", request));
        }
        finally
        {
            await DropFailingDeliveryTriggerAsync();
        }

        using var retryResponse = await client.PostAsJsonAsync("/messages", request);
        Assert.Equal(HttpStatusCode.Accepted, retryResponse.StatusCode);

        var retryReceipt = await retryResponse.Content.ReadFromJsonAsync<CreateMessageResponse>();
        Assert.NotNull(retryReceipt);
        Assert.Equal(2, retryReceipt.DeliveryCount);
    }

    private async Task EnsureDatabaseReadyAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();

        await dbContext.Database.MigrateAsync();
    }

    private async Task InstallFailingDeliveryTriggerAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            CREATE OR REPLACE FUNCTION notifyrail_fail_delivery_insert()
            RETURNS trigger AS $$
            BEGIN
                IF NEW.recipient = '__notifyrail_fail_delivery_insert__' THEN
                    RAISE EXCEPTION 'forced delivery insert failure';
                END IF;

                RETURN NEW;
            END;
            $$ LANGUAGE plpgsql;

            DROP TRIGGER IF EXISTS notifyrail_fail_delivery_insert ON deliveries;

            CREATE TRIGGER notifyrail_fail_delivery_insert
            BEFORE INSERT ON deliveries
            FOR EACH ROW
            EXECUTE FUNCTION notifyrail_fail_delivery_insert();
            """);
    }

    private async Task DropFailingDeliveryTriggerAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();

        await dbContext.Database.ExecuteSqlRawAsync(
            """
            DROP TRIGGER IF EXISTS notifyrail_fail_delivery_insert ON deliveries;
            DROP FUNCTION IF EXISTS notifyrail_fail_delivery_insert();
            """);
    }

    private static async Task AssertRequestFailsAsync(Task<HttpResponseMessage> requestTask)
    {
        try
        {
            using var response = await requestTask;

            Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
        }
        catch (Exception exception) when (exception is HttpRequestException or InvalidOperationException)
        {
        }
    }

    private static CreateMessageRequest ValidRequest(
        string idempotencyKey,
        string body = "Your order is ready.",
        string[]? recipients = null)
    {
        return new CreateMessageRequest(
            Type: "transactional",
            Channel: "sms",
            SenderTitle: "NotifyRail",
            Body: body,
            Recipients: recipients ?? ["+905551111111", "+905552222222"],
            IdempotencyKey: idempotencyKey);
    }
}
