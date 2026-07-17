using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using NotifyRail.Api.Features.ApiClients.CreateApiClient;
using NotifyRail.Api.Features.Deliveries.Worker;
using NotifyRail.Api.Features.Messages.CreateMessage;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Tests;

public sealed class GetMessageReportEndpointIntegrationTests
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string OperatorCredential = "get-report-test-operator-credential";

    private readonly WebApplicationFactory<Program> _factory;

    public GetMessageReportEndpointIntegrationTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory
            .WithoutHostedServices()
            .WithWebHostBuilder(builder =>
                builder.UseSetting("Authentication:Operator:Credential", OperatorCredential));
    }

    public void Dispose()
    {
        _factory.Dispose();
    }

    [Fact]
    public async Task GetMessageReport_ReturnsUnauthorized_WhenApiKeyIsMissing()
    {
        using var client = _factory.CreateClient();

        using var response = await client.GetAsync($"/messages/{Guid.NewGuid()}/report");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task GetMessageReport_ReturnsDeliveryStatusCounts()
    {
        await EnsureDatabaseReadyAsync();
        await ResetDatabaseAsync();

        using var client = await CreateAuthenticatedClientAsync();
        var receipt = await CreateMessageAsync(client);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var worker = scope.ServiceProvider.GetRequiredService<DeliveryWorker>();
            var processed = await worker.ProcessBatchAsync(
                DateTimeOffset.UtcNow,
                CancellationToken.None);

            Assert.Equal(1, processed);
        }

        using var response = await client.GetAsync($"/messages/{receipt.MessageId}/report");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(receipt.MessageId, body.RootElement.GetProperty("message_id").GetGuid());
        Assert.Equal(3, body.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(2, body.RootElement.GetProperty("queued").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("processing").GetInt32());
        Assert.Equal(1, body.RootElement.GetProperty("sent").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("delivered").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("retry_scheduled").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("failed").GetInt32());
        Assert.Equal(0, body.RootElement.GetProperty("expired").GetInt32());
    }

    [Fact]
    public async Task GetMessageReport_ReturnsNotFound_WhenMessageDoesNotExist()
    {
        await EnsureDatabaseReadyAsync();
        await ResetDatabaseAsync();

        using var client = await CreateAuthenticatedClientAsync();
        using var response = await client.GetAsync($"/messages/{Guid.NewGuid()}/report");
        using var body = JsonDocument.Parse(await response.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        Assert.Equal("message not found", body.RootElement.GetProperty("error").GetString());
    }

    [Fact]
    public async Task GetMessageReport_ReturnsNotFound_ForAnotherApiClientsMessage()
    {
        await EnsureDatabaseReadyAsync();
        await ResetDatabaseAsync();

        using var ownerClient = await CreateAuthenticatedClientAsync();
        using var otherClient = await CreateAuthenticatedClientAsync();
        var receipt = await CreateMessageAsync(ownerClient);

        using var response = await otherClient.GetAsync($"/messages/{receipt.MessageId}/report");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    private static async Task<CreateMessageResponse> CreateMessageAsync(HttpClient client)
    {
        using var response = await client.PostAsJsonAsync(
            "/messages",
            new CreateMessageRequest(
                Type: "campaign",
                Channel: "sms",
                SenderTitle: "NotifyRail",
                Body: "Campaign update.",
                Recipients:
                [
                    "+905551111111",
                    "+905552222222",
                    "+905553333333",
                ],
                IdempotencyKey: $"get-report-{Guid.NewGuid()}"));
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

    private async Task<HttpClient> CreateAuthenticatedClientAsync()
    {
        using var operatorClient = _factory.CreateClient();
        operatorClient.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Operator", OperatorCredential);
        using var response = await operatorClient.PostAsJsonAsync(
            "/management/api-clients",
            new { name = $"Get Report {Guid.NewGuid()}" });
        response.EnsureSuccessStatusCode();
        var apiClient = await response.Content.ReadFromJsonAsync<CreateApiClientResponse>();
        Assert.NotNull(apiClient);

        var client = _factory.CreateClient();
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("ApiKey", apiClient.ApiKey);
        return client;
    }

    private async Task ResetDatabaseAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();

        await dbContext.Database.ExecuteSqlRawAsync(
            "TRUNCATE otp_challenges, delivery_attempts, deliveries, messages;");
    }
}
