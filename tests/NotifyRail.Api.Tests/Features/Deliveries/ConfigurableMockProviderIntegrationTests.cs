using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using NotifyRail.Api.Features.Deliveries.Worker;
using NotifyRail.Api.Features.Messages.CreateMessage;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Tests;

public sealed class ConfigurableMockProviderIntegrationTests
    : IClassFixture<WebApplicationFactory<Program>>, IAsyncDisposable
{
    private readonly WebApplicationFactory<Program> _factory;

    public ConfigurableMockProviderIntegrationTests(
        WebApplicationFactory<Program> factory)
    {
        _factory = factory
            .WithoutHostedServices()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureAppConfiguration((_, configuration) =>
                {
                    configuration.AddInMemoryCollection(new Dictionary<string, string?>
                    {
                        ["DeliveryWorker:BatchSize"] = "3",
                        ["MockProvider:Rules:0:Recipient"] = "+905552222222",
                        ["MockProvider:Rules:0:Outcomes:0"] = "retryable_failure",
                        ["MockProvider:Rules:0:Outcomes:1"] = "accepted",
                        ["MockProvider:Rules:1:Recipient"] = "+905553333333",
                        ["MockProvider:Rules:1:Outcomes:0"] = "permanent_failure",
                    });
                });
            });
    }

    public async ValueTask DisposeAsync()
    {
        await _factory.DisposeAsync();
    }

    [Fact]
    public async Task ConfiguredOutcomes_DriveDeliveryLifecycleAcrossAttempts()
    {
        await ResetDatabaseAsync();

        using var client = _factory.CreateClient();
        var receipt = await CreateMessageAsync(client);

        await using (var scope = _factory.Services.CreateAsyncScope())
        {
            var worker = scope.ServiceProvider.GetRequiredService<DeliveryWorker>();
            var now = DateTimeOffset.UtcNow;

            Assert.Equal(
                3,
                await worker.ProcessBatchAsync(now, CancellationToken.None));
            Assert.Equal(
                1,
                await worker.ProcessBatchAsync(now.AddMinutes(2), CancellationToken.None));
        }

        using var reportResponse = await client.GetAsync(
            $"/messages/{receipt.MessageId}/report");
        using var report = JsonDocument.Parse(
            await reportResponse.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, reportResponse.StatusCode);
        Assert.Equal(3, report.RootElement.GetProperty("total").GetInt32());
        Assert.Equal(2, report.RootElement.GetProperty("sent").GetInt32());
        Assert.Equal(1, report.RootElement.GetProperty("failed").GetInt32());
        Assert.Equal(0, report.RootElement.GetProperty("retry_scheduled").GetInt32());

        using var deliveriesResponse = await client.GetAsync(
            $"/messages/{receipt.MessageId}/deliveries");
        using var deliveriesBody = JsonDocument.Parse(
            await deliveriesResponse.Content.ReadAsStreamAsync());

        Assert.Equal(HttpStatusCode.OK, deliveriesResponse.StatusCode);
        var deliveries = deliveriesBody.RootElement
            .GetProperty("deliveries")
            .EnumerateArray()
            .ToDictionary(delivery => delivery.GetProperty("recipient").GetString()!);

        var retried = deliveries["+905552222222"];
        Assert.Equal("sent", retried.GetProperty("status").GetString());
        Assert.Equal(2, retried.GetProperty("attempt_count").GetInt32());
        Assert.Equal(
            ["retryable_failure", "accepted"],
            retried.GetProperty("attempts")
                .EnumerateArray()
                .Select(attempt => attempt.GetProperty("outcome").GetString()!)
                .ToArray());

        var permanentlyFailed = deliveries["+905553333333"];
        Assert.Equal("failed", permanentlyFailed.GetProperty("status").GetString());
        var failedAttempt = Assert.Single(
            permanentlyFailed.GetProperty("attempts").EnumerateArray());
        Assert.Equal("permanent_failure", failedAttempt.GetProperty("outcome").GetString());
        Assert.Equal(
            "mock_permanent_failure",
            failedAttempt.GetProperty("error_code").GetString());
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
                IdempotencyKey: $"mock-scenarios-{Guid.NewGuid()}"));
        var receipt = await response.Content.ReadFromJsonAsync<CreateMessageResponse>();

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        return Assert.IsType<CreateMessageResponse>(receipt);
    }

    private async Task ResetDatabaseAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();

        await dbContext.Database.MigrateAsync();
        await dbContext.Database.ExecuteSqlRawAsync(
            "TRUNCATE delivery_attempts, deliveries, messages;");
    }
}
