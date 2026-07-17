using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotifyRail.Api.Features.Messages.CreateMessage;
using NotifyRail.Api.Features.ApiClients.Persistence;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Tests;

public sealed class CreateMessageEndpointIntegrationTests
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string FailingRecipient = "__notifyrail_fail_delivery_insert__";
    private const int MaxCreateMessageBodyBytes = 1 << 20;

    private readonly WebApplicationFactory<Program> _factory;

    public CreateMessageEndpointIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory.WithoutHostedServices();
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    [Fact]
    public async Task CreateMessage_AssignsMessageToLegacyApiClient()
    {
        await EnsureDatabaseReadyAsync();

        using var client = _factory.CreateClient();
        using var response = await client.PostAsJsonAsync(
            "/messages",
            ValidRequest($"legacy-owner-{Guid.NewGuid()}"));
        var receipt = await response.Content.ReadFromJsonAsync<CreateMessageResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(receipt);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
        var apiClientId = await dbContext.Messages
            .AsNoTracking()
            .Where(message => message.Id == receipt.MessageId)
            .Select(message => message.ApiClientId)
            .SingleAsync();

        Assert.Equal(ApiClient.LegacyId, apiClientId);
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
    public async Task CreateMessage_ReplaysSameResponse_WhenRequestMatchesAfterNormalization()
    {
        await EnsureDatabaseReadyAsync();

        using var client = _factory.CreateClient();
        var idempotencyKey = $"normalized-{Guid.NewGuid()}";
        var request = ValidRequest($" {idempotencyKey} ") with
        {
            SenderTitle = " NotifyRail ",
            Recipients = [" +905551111111 ", "+905552222222 "],
            ReportLabel = " Shipping Updates ",
        };

        using var firstResponse = await client.PostAsJsonAsync("/messages", request);
        using var secondResponse = await client.PostAsJsonAsync(
            "/messages",
            ValidRequest(idempotencyKey) with
            {
                ReportLabel = "Shipping Updates",
            });

        Assert.Equal(HttpStatusCode.Accepted, firstResponse.StatusCode);
        Assert.Equal(HttpStatusCode.Accepted, secondResponse.StatusCode);

        var firstReceipt = await firstResponse.Content.ReadFromJsonAsync<CreateMessageResponse>();
        var secondReceipt = await secondResponse.Content.ReadFromJsonAsync<CreateMessageResponse>();

        Assert.NotNull(firstReceipt);
        Assert.NotNull(secondReceipt);
        Assert.Equal(firstReceipt.MessageId, secondReceipt.MessageId);
        Assert.Equal(firstReceipt.DeliveryCount, secondReceipt.DeliveryCount);
        Assert.Equal(firstReceipt.CreatedAt, secondReceipt.CreatedAt);
    }

    [Fact]
    public async Task CreateMessage_PersistsScheduledAtAsUtc_WhenRequestUsesNonUtcOffset()
    {
        await EnsureDatabaseReadyAsync();

        using var client = _factory.CreateClient();
        var scheduledAt = new DateTimeOffset(
            2026,
            7,
            6,
            15,
            30,
            0,
            TimeSpan.FromHours(3));
        var request = ValidRequest($"scheduled-offset-{Guid.NewGuid()}") with
        {
            ScheduledAt = scheduledAt,
        };

        using var response = await client.PostAsJsonAsync("/messages", request);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);

        var receipt = await response.Content.ReadFromJsonAsync<CreateMessageResponse>();
        Assert.NotNull(receipt);

        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
        var persistedScheduledAt = await dbContext.Messages
            .AsNoTracking()
            .Where(message => message.Id == receipt.MessageId)
            .Select(message => message.ScheduledAt)
            .SingleAsync();

        Assert.Equal(scheduledAt.ToUniversalTime(), persistedScheduledAt);
        Assert.Equal(TimeSpan.Zero, persistedScheduledAt!.Value.Offset);
    }

    [Fact]
    public async Task CreateMessage_ReturnsBadRequest_WhenDuplicateRecipientsMatchAfterTrim()
    {
        await EnsureDatabaseReadyAsync();

        using var client = _factory.CreateClient();
        var request = ValidRequest($"invalid-duplicate-{Guid.NewGuid()}") with
        {
            Recipients = [" +905551111111 ", "+905551111111"],
        };

        using var response = await client.PostAsJsonAsync("/messages", request);
        var error = await response.Content.ReadFromJsonAsync<CreateMessageErrorResponse>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal("recipient +905551111111 must not be repeated", error.Error);
    }

    [Fact]
    public async Task CreateMessage_ReturnsBadRequest_WhenMessageTypeIsInvalid()
    {
        await EnsureDatabaseReadyAsync();

        using var client = _factory.CreateClient();
        var request = ValidRequest($"invalid-type-{Guid.NewGuid()}") with
        {
            Type = "email",
        };

        using var response = await client.PostAsJsonAsync("/messages", request);
        var error = await response.Content.ReadFromJsonAsync<CreateMessageErrorResponse>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal("type must be one of: otp, transactional, campaign", error.Error);
    }

    [Theory]
    [InlineData("")]
    [InlineData("""{"type":""")]
    [InlineData(
        """
        {
            "type":"transactional",
            "channel":"sms",
            "sender_title":"NotifyRail",
            "body":"Hello",
            "recipients":["+905551111111"],
            "idempotency_key":"request-1",
            "unexpected":true
        }
        """)]
    [InlineData(
        """
        {
            "type":"transactional",
            "channel":"sms",
            "sender_title":"NotifyRail",
            "body":"Hello",
            "recipients":["+905551111111"],
            "idempotency_key":"request-1"
        }{}
        """)]
    public async Task CreateMessage_ReturnsBadRequest_WhenJsonBodyIsInvalid(string body)
    {
        using var client = _factory.CreateClient();
        using var response = await client.PostAsync(
            "/messages",
            new StringContent(body, Encoding.UTF8, "application/json"));

        var error = await response.Content.ReadFromJsonAsync<CreateMessageErrorResponse>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(error);
        Assert.StartsWith("invalid JSON body", error.Error, StringComparison.Ordinal);
    }

    [Fact]
    public async Task CreateMessage_ReturnsBadRequest_WhenJsonBodyExceedsSizeLimit()
    {
        using var client = _factory.CreateClient();
        var body = "{\"body\":\"" + new string('x', MaxCreateMessageBodyBytes) + "\"}";

        using var response = await client.PostAsync(
            "/messages",
            new StringContent(body, Encoding.UTF8, "application/json"));

        var error = await response.Content.ReadFromJsonAsync<CreateMessageErrorResponse>();

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.NotNull(error);
        Assert.Equal("invalid JSON body: request body is too large", error.Error);
    }

    [Fact]
    public async Task CreateMessage_AcceptsJsonBodyAtSizeLimit()
    {
        await EnsureDatabaseReadyAsync();

        using var client = _factory.CreateClient();
        var body = $$"""
            {
                "type":"transactional",
                "channel":"sms",
                "sender_title":"NotifyRail",
                "body":"Hello",
                "recipients":["+905551111111"],
                "idempotency_key":"size-limit-{{Guid.NewGuid()}}"
            }
            """;
        body += new string(' ', MaxCreateMessageBodyBytes - Encoding.UTF8.GetByteCount(body));

        using var response = await client.PostAsync(
            "/messages",
            new StringContent(body, Encoding.UTF8, "application/json"));

        var receipt = await response.Content.ReadFromJsonAsync<CreateMessageResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        Assert.NotNull(receipt);
        Assert.Equal(1, receipt.DeliveryCount);
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
