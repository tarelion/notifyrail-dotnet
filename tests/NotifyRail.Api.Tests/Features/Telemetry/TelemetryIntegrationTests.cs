using System.Diagnostics;
using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using NotifyRail.Api.Features.Deliveries.Worker;
using NotifyRail.Api.Features.Messages.CreateMessage;
using NotifyRail.Api.Features.Webhooks.Worker;
using NotifyRail.Api.Telemetry;
using OpenTelemetry;
using OpenTelemetry.Trace;

namespace NotifyRail.Api.Tests;

public sealed class TelemetryIntegrationTests
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string MessageBody = "sensitive message body";
    private const string Recipient = "+905551234567";

    private readonly List<Activity> _activities = [];
    private readonly WebApplicationFactory<Program> _factory;
    private readonly TracerProvider _tracerProvider;

    public TelemetryIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _tracerProvider = Sdk.CreateTracerProviderBuilder()
            .AddSource(NotifyRailTelemetry.ActivitySourceName)
            .SetSampler(new AlwaysOnSampler())
            .AddInMemoryExporter(_activities)
            .Build();
        _factory = factory
            .WithMessageApiAuthentication()
            .WithWebHostBuilder(builder =>
            {
                builder.ConfigureTestServices(services =>
                {
                    var workers = services
                        .Where(descriptor =>
                            descriptor.ServiceType == typeof(IHostedService)
                            && (descriptor.ImplementationType
                                    == typeof(DeliveryWorkerBackgroundService)
                                || descriptor.ImplementationType
                                    == typeof(WebhookWorkerBackgroundService)))
                        .ToArray();
                    foreach (var worker in workers)
                    {
                        services.Remove(worker);
                    }

                });
            });
    }

    public void Dispose()
    {
        _factory.Dispose();
        _tracerProvider.Dispose();
    }

    [Fact]
    public async Task CreateMessage_ExportsCorrelatedSafeMessageIntakeSpan()
    {
        using var client = await _factory.CreateAuthenticatedMessageClientAsync(
            "Telemetry client");
        var apiKey = client.DefaultRequestHeaders.Authorization!.Parameter!;

        using var response = await client.PostAsJsonAsync(
            "/messages",
            new
            {
                type = "transactional",
                channel = "sms",
                sender_title = "NotifyRail",
                body = MessageBody,
                recipients = new[] { Recipient },
                idempotency_key = $"telemetry-{Guid.NewGuid()}",
            });

        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var receipt = await response.Content.ReadFromJsonAsync<CreateMessageResponse>();
        Assert.NotNull(receipt);
        _tracerProvider.ForceFlush();

        var activity = Assert.Single(
            _activities,
            candidate => candidate.OperationName == NotifyRailTelemetry.MessageIntakeActivity);
        Assert.Equal(
            receipt.MessageId.ToString(),
            activity.GetTagItem(NotifyRailTelemetry.MessageIdTag));
        Assert.NotNull(activity.GetTagItem(NotifyRailTelemetry.ApiClientIdTag));
        Assert.Equal(1, activity.GetTagItem(NotifyRailTelemetry.DeliveryCountTag));
        Assert.Equal("+9*********67", activity.GetTagItem(NotifyRailTelemetry.RecipientTag));

        var exported = string.Join(
            '\n',
            activity.TagObjects.Select(tag => $"{tag.Key}={tag.Value}"));
        Assert.DoesNotContain(Recipient, exported, StringComparison.Ordinal);
        Assert.DoesNotContain(MessageBody, exported, StringComparison.Ordinal);
        Assert.DoesNotContain(apiKey, exported, StringComparison.Ordinal);
    }
}
