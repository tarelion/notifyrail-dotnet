using System.Diagnostics;
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
using NotifyRail.Api.Features.Deliveries.Worker;
using NotifyRail.Api.Features.ApiClients.GetCurrentApiClient;
using NotifyRail.Api.Features.Messages.GetMessageDeliveries;
using NotifyRail.Api.Features.Messages.CreateMessage;
using NotifyRail.Api.Features.Otp.SendOtp;
using NotifyRail.Api.Features.Webhooks.Dispatch;
using NotifyRail.Api.Features.Webhooks.RegisterWebhookEndpoint;
using NotifyRail.Api.Features.Webhooks.ReplayDeadWebhookEvent;
using NotifyRail.Api.Features.Webhooks.Secrets;
using NotifyRail.Api.Features.Webhooks.Worker;
using NotifyRail.Api.Telemetry;
using NotifyRail.Api.Infrastructure.Persistence;
using OpenTelemetry;
using OpenTelemetry.Logs;
using OpenTelemetry.Trace;

namespace NotifyRail.Api.Tests;

public sealed class TelemetryIntegrationTests
    : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private const string MessageBody = "sensitive message body";
    private const string Recipient = "+905551234567";
    private const string CallbackSecret = "telemetry-callback-secret";
    private const string RemoteResponseBody = "sensitive remote response body";

    private readonly List<Activity> _activities = [];
    private readonly List<LogRecord> _logs = [];
    private readonly WebApplicationFactory<Program> _factory;
    private readonly TracerProvider _tracerProvider;
    private readonly SuccessfulWebhookHandler _webhookHandler = new();

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
                builder.UseSetting(
                    "MockProviderCallback:Secret",
                    CallbackSecret);
                builder.UseSetting(
                    "WebhookWorker:AutomaticRetryWindow",
                    "00:02:00");
                builder.UseSetting("WebhookWorker:JitterRatio", "0");
                builder.ConfigureLogging(logging => logging.AddOpenTelemetry(
                    options =>
                    {
                        options.IncludeScopes = true;
                        options.ParseStateValues = true;
                        options.AddInMemoryExporter(_logs);
                    }));
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

                    services.RemoveAll<WebhookDispatcher>();
                    services.AddScoped(serviceProvider => new WebhookDispatcher(
                        new HttpClient(_webhookHandler, disposeHandler: false),
                        serviceProvider.GetRequiredService<IWebhookSecretProtector>()));
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
        await ResetDatabaseAsync();
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
            activity.TagObjects.Select(tag => $"{tag.Key}={tag.Value}"))
            + string.Join(
                '\n',
                _logs.SelectMany(log => log.Attributes ?? [])
                    .Select(attribute => $"{attribute.Key}={attribute.Value}"));
        Assert.DoesNotContain(Recipient, exported, StringComparison.Ordinal);
        Assert.DoesNotContain(MessageBody, exported, StringComparison.Ordinal);
        Assert.DoesNotContain(apiKey, exported, StringComparison.Ordinal);
    }

    [Fact]
    public async Task DeliveryWorker_LinksProcessingSpanToPersistedMessageTrace()
    {
        await ResetDatabaseAsync();
        using var client = await _factory.CreateAuthenticatedMessageClientAsync(
            "Delivery telemetry client");
        using var response = await client.PostAsJsonAsync(
            "/messages",
            new
            {
                type = "transactional",
                channel = "sms",
                sender_title = "NotifyRail",
                body = MessageBody,
                recipients = new[] { Recipient },
                idempotency_key = $"delivery-telemetry-{Guid.NewGuid()}",
            });
        response.EnsureSuccessStatusCode();
        var receipt = await response.Content.ReadFromJsonAsync<CreateMessageResponse>();
        Assert.NotNull(receipt);
        var intake = Assert.Single(
            _activities,
            candidate => candidate.OperationName == NotifyRailTelemetry.MessageIntakeActivity);

        await using var scope = _factory.Services.CreateAsyncScope();
        var worker = scope.ServiceProvider.GetRequiredService<DeliveryWorker>();
        Assert.Equal(
            1,
            await worker.ProcessBatchAsync(
                DateTimeOffset.UtcNow,
                CancellationToken.None));
        _tracerProvider.ForceFlush();

        var processing = Assert.Single(
            _activities,
            candidate => candidate.OperationName == NotifyRailTelemetry.DeliveryProcessActivity);
        Assert.Contains(
            processing.Links,
            link => link.Context.TraceId == intake.TraceId);
        Assert.Equal(
            receipt.MessageId.ToString(),
            processing.GetTagItem(NotifyRailTelemetry.MessageIdTag));
        Assert.NotNull(processing.GetTagItem(NotifyRailTelemetry.ApiClientIdTag));
        Assert.NotNull(processing.GetTagItem(NotifyRailTelemetry.DeliveryIdTag));
        Assert.Equal("+9*********67", processing.GetTagItem(NotifyRailTelemetry.RecipientTag));
    }

    [Fact]
    public async Task CallbackAndWebhookWorker_LinkSpansAcrossPersistedBoundaries()
    {
        await ResetDatabaseAsync();
        using var client = await _factory.CreateAuthenticatedMessageClientAsync(
            "Webhook telemetry client");
        var currentClient = await client.GetFromJsonAsync<GetCurrentApiClientResponse>(
            "/api-client");
        Assert.NotNull(currentClient);
        string webhookSecret;
        await using (var registrationScope = _factory.Services.CreateAsyncScope())
        {
            var registrar = registrationScope.ServiceProvider
                .GetRequiredService<WebhookEndpointRegistrar>();
            var registration = await registrar.RegisterAsync(
                currentClient.ApiClientId,
                "https://hooks.example.com/notifyrail",
                CancellationToken.None);
            Assert.NotNull(registration);
            webhookSecret = Assert.IsType<string>(registration.WebhookSecret);
        }

        using var createResponse = await client.PostAsJsonAsync(
            "/messages",
            new
            {
                type = "transactional",
                channel = "sms",
                sender_title = "NotifyRail",
                body = MessageBody,
                recipients = new[] { Recipient },
                idempotency_key = $"webhook-telemetry-{Guid.NewGuid()}",
            });
        createResponse.EnsureSuccessStatusCode();
        var receipt = await createResponse.Content
            .ReadFromJsonAsync<CreateMessageResponse>();
        Assert.NotNull(receipt);
        var intake = Assert.Single(
            _activities,
            candidate => candidate.OperationName
                == NotifyRailTelemetry.MessageIntakeActivity);

        await using (var deliveryScope = _factory.Services.CreateAsyncScope())
        {
            var deliveryWorker = deliveryScope.ServiceProvider
                .GetRequiredService<DeliveryWorker>();
            Assert.Equal(1, await deliveryWorker.ProcessBatchAsync(
                DateTimeOffset.UtcNow,
                CancellationToken.None));
        }

        var deliveries = await client
            .GetFromJsonAsync<GetMessageDeliveriesResponse>(
                $"/messages/{receipt.MessageId}/deliveries");
        var delivery = Assert.Single(deliveries!.Deliveries);
        using var callbackRequest = CreateSignedCallbackRequest(
            delivery.ProviderMessageId!,
            "delivered");
        using var callbackResponse = await client.SendAsync(callbackRequest);
        callbackResponse.EnsureSuccessStatusCode();

        await using (var webhookScope = _factory.Services.CreateAsyncScope())
        {
            var webhookWorker = webhookScope.ServiceProvider
                .GetRequiredService<WebhookWorker>();
            Assert.Equal(1, await webhookWorker.ProcessBatchAsync(
                DateTimeOffset.UtcNow,
                CancellationToken.None));
            Assert.Equal(1, await webhookWorker.ProcessBatchAsync(
                DateTimeOffset.UtcNow,
                CancellationToken.None));
        }
        _tracerProvider.ForceFlush();

        var callback = Assert.Single(
            _activities,
            candidate => candidate.OperationName
                == NotifyRailTelemetry.ProviderCallbackActivity);
        Assert.Contains(
            callback.Links,
            link => link.Context.TraceId == intake.TraceId);
        Assert.Equal(
            delivery.DeliveryId.ToString(),
            callback.GetTagItem(NotifyRailTelemetry.DeliveryIdTag));

        var eventCreationSpans = _activities
            .Where(candidate => candidate.OperationName
                == NotifyRailTelemetry.WebhookEventCreateActivity)
            .ToArray();
        Assert.Equal(2, eventCreationSpans.Length);
        Assert.Contains(
            eventCreationSpans,
            activity => activity.ParentSpanId == callback.SpanId);

        var dispatchSpans = _activities
            .Where(candidate => candidate.OperationName
                == NotifyRailTelemetry.WebhookDispatchActivity)
            .ToArray();
        Assert.Equal(2, dispatchSpans.Length);
        Assert.All(dispatchSpans, dispatch =>
        {
            Assert.Single(dispatch.Links);
            Assert.Contains(
                eventCreationSpans,
                creation => creation.TraceId == dispatch.Links.Single().Context.TraceId);
            Assert.NotNull(
                dispatch.GetTagItem(NotifyRailTelemetry.WebhookEventIdTag));
        });
        Assert.Equal(2, _webhookHandler.RequestCount);

        var logAttributes = _logs
            .SelectMany(log => log.Attributes ?? [])
            .ToArray();
        Assert.Contains(
            logAttributes,
            attribute => attribute.Key == NotifyRailTelemetry.ApiClientIdTag);
        Assert.Contains(
            logAttributes,
            attribute => attribute.Key == NotifyRailTelemetry.MessageIdTag);
        Assert.Contains(
            logAttributes,
            attribute => attribute.Key == NotifyRailTelemetry.DeliveryIdTag);
        Assert.Contains(
            logAttributes,
            attribute => attribute.Key == NotifyRailTelemetry.WebhookEventIdTag);
        Assert.Contains(
            logAttributes,
            attribute => attribute.Key == NotifyRailTelemetry.WebhookAttemptIdTag);
        var exportedLogs = string.Join(
            '\n',
            _logs.Select(log => string.Join(
                ' ',
                (log.Attributes ?? [])
                    .Select(attribute => $"{attribute.Key}={attribute.Value}"))));
        Assert.DoesNotContain(Recipient, exportedLogs, StringComparison.Ordinal);
        Assert.DoesNotContain(MessageBody, exportedLogs, StringComparison.Ordinal);
        Assert.DoesNotContain(CallbackSecret, exportedLogs, StringComparison.Ordinal);
        Assert.DoesNotContain(webhookSecret, exportedLogs, StringComparison.Ordinal);
        Assert.DoesNotContain(RemoteResponseBody, exportedLogs, StringComparison.Ordinal);
        Assert.All(
            _webhookHandler.Signatures,
            signature => Assert.DoesNotContain(
                signature,
                exportedLogs,
                StringComparison.Ordinal));
    }

    [Fact]
    public async Task WebhookRetryDeathAndReplay_RemainLinkedToTheEventTrace()
    {
        await ResetDatabaseAsync();
        _webhookHandler.RespondWith(
            HttpStatusCode.InternalServerError,
            HttpStatusCode.InternalServerError,
            HttpStatusCode.NoContent);
        using var client = await _factory.CreateAuthenticatedMessageClientAsync(
            "Webhook recovery telemetry client");
        var currentClient = await client.GetFromJsonAsync<GetCurrentApiClientResponse>(
            "/api-client");
        Assert.NotNull(currentClient);
        await using (var registrationScope = _factory.Services.CreateAsyncScope())
        {
            var registrar = registrationScope.ServiceProvider
                .GetRequiredService<WebhookEndpointRegistrar>();
            Assert.NotNull(await registrar.RegisterAsync(
                currentClient.ApiClientId,
                "https://hooks.example.com/notifyrail",
                CancellationToken.None));
        }

        using var createResponse = await client.PostAsJsonAsync(
            "/messages",
            new
            {
                type = "transactional",
                channel = "sms",
                sender_title = "NotifyRail",
                body = MessageBody,
                recipients = new[] { Recipient },
                idempotency_key = $"webhook-recovery-{Guid.NewGuid()}",
            });
        createResponse.EnsureSuccessStatusCode();
        await using (var deliveryScope = _factory.Services.CreateAsyncScope())
        {
            var deliveryWorker = deliveryScope.ServiceProvider
                .GetRequiredService<DeliveryWorker>();
            Assert.Equal(1, await deliveryWorker.ProcessBatchAsync(
                DateTimeOffset.UtcNow,
                CancellationToken.None));
        }
        var eventCreation = Assert.Single(
            _activities,
            candidate => candidate.OperationName
                == NotifyRailTelemetry.WebhookEventCreateActivity);
        var eventId = Guid.Parse(
            (string)eventCreation.GetTagItem(
                NotifyRailTelemetry.WebhookEventIdTag)!);
        var firstAttemptAt = DateTimeOffset.UtcNow;

        await using (var firstAttemptScope = _factory.Services.CreateAsyncScope())
        {
            var worker = firstAttemptScope.ServiceProvider
                .GetRequiredService<WebhookWorker>();
            Assert.Equal(1, await worker.ProcessBatchAsync(
                firstAttemptAt,
                CancellationToken.None));
        }
        var retry = Assert.Single(
            _activities,
            candidate => candidate.OperationName
                    == NotifyRailTelemetry.WebhookDispatchActivity
                && Equals(
                    candidate.GetTagItem(
                        NotifyRailTelemetry.WebhookDispatchStatusTag),
                    "retry_scheduled"));

        await using (var secondAttemptScope = _factory.Services.CreateAsyncScope())
        {
            var worker = secondAttemptScope.ServiceProvider
                .GetRequiredService<WebhookWorker>();
            Assert.Equal(1, await worker.ProcessBatchAsync(
                firstAttemptAt.AddSeconds(90),
                CancellationToken.None));
        }
        var death = Assert.Single(
            _activities,
            candidate => candidate.OperationName
                    == NotifyRailTelemetry.WebhookDispatchActivity
                && Equals(
                    candidate.GetTagItem(
                        NotifyRailTelemetry.WebhookDispatchStatusTag),
                    "dead"));
        Assert.Equal(
            eventId.ToString(),
            death.GetTagItem(NotifyRailTelemetry.WebhookEventIdTag));

        await using (var replayScope = _factory.Services.CreateAsyncScope())
        {
            var replayer = replayScope.ServiceProvider
                .GetRequiredService<WebhookEventReplayer>();
            Assert.NotNull(await replayer.ReplayAsync(
                eventId,
                firstAttemptAt.AddMinutes(2),
                CancellationToken.None));
        }
        var replay = Assert.Single(
            _activities,
            candidate => candidate.OperationName
                == NotifyRailTelemetry.WebhookReplayActivity);
        Assert.Contains(
            replay.Links,
            link => link.Context.TraceId == eventCreation.TraceId);

        await using (var replayAttemptScope = _factory.Services.CreateAsyncScope())
        {
            var worker = replayAttemptScope.ServiceProvider
                .GetRequiredService<WebhookWorker>();
            Assert.Equal(1, await worker.ProcessBatchAsync(
                firstAttemptAt.AddMinutes(2),
                CancellationToken.None));
        }
        var succeededReplay = Assert.Single(
            _activities,
            candidate => candidate.OperationName
                    == NotifyRailTelemetry.WebhookDispatchActivity
                && Equals(
                    candidate.GetTagItem(
                        NotifyRailTelemetry.WebhookDispatchStatusTag),
                    "succeeded"));
        Assert.Contains(
            succeededReplay.Links,
            link => link.Context.TraceId == replay.TraceId);
        Assert.NotEqual(retry.SpanId, death.SpanId);
    }

    [Fact]
    public async Task SendOtp_ExportsSafeMessageIntakeTelemetryWithoutDebugCode()
    {
        await ResetDatabaseAsync();
        using var client = await _factory.CreateAuthenticatedMessageClientAsync(
            "OTP telemetry client");
        using var response = await client.PostAsJsonAsync(
            "/otp/send",
            new
            {
                recipient = Recipient,
                idempotency_key = $"otp-telemetry-{Guid.NewGuid()}",
            });
        response.EnsureSuccessStatusCode();
        var result = await response.Content.ReadFromJsonAsync<SendOtpResponse>();
        Assert.NotNull(result);
        _tracerProvider.ForceFlush();

        var intake = Assert.Single(
            _activities,
            candidate => candidate.OperationName
                == NotifyRailTelemetry.MessageIntakeActivity);
        Assert.Equal(
            result.MessageId.ToString(),
            intake.GetTagItem(NotifyRailTelemetry.MessageIdTag));
        Assert.Equal(
            "+9*********67",
            intake.GetTagItem(NotifyRailTelemetry.RecipientTag));

        var exported = string.Join(
            '\n',
            _activities.SelectMany(activity => activity.TagObjects)
                .Select(tag => $"{tag.Key}={tag.Value}"))
            + string.Join(
                '\n',
                _logs.SelectMany(log => log.Attributes ?? [])
                    .Select(attribute => $"{attribute.Key}={attribute.Value}"));
        Assert.DoesNotContain(result.DebugCode, exported, StringComparison.Ordinal);
        Assert.DoesNotContain(Recipient, exported, StringComparison.Ordinal);
    }

    private async Task ResetDatabaseAsync()
    {
        await using var scope = _factory.Services.CreateAsyncScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<NotifyRailDbContext>();
        await dbContext.Database.MigrateAsync(CancellationToken.None);
        await dbContext.Database.ExecuteSqlRawAsync(
            "TRUNCATE webhook_attempts, webhook_events, webhook_secrets, " +
            "webhook_endpoints, otp_challenges, delivery_attempts, deliveries, " +
            "messages, api_keys, api_clients CASCADE;",
            CancellationToken.None);
        _activities.Clear();
        _logs.Clear();
        _webhookHandler.Reset();
    }

    private static HttpRequestMessage CreateSignedCallbackRequest(
        string providerMessageId,
        string status)
    {
        var body = JsonSerializer.SerializeToUtf8Bytes(new
        {
            provider_message_id = providerMessageId,
            status,
        });
        var timestamp = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString();
        var signedPayload = Encoding.UTF8.GetBytes($"{timestamp}.")
            .Concat(body)
            .ToArray();
        var signature = Convert.ToHexStringLower(
            HMACSHA256.HashData(
                Encoding.UTF8.GetBytes(CallbackSecret),
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

    private sealed class SuccessfulWebhookHandler : HttpMessageHandler
    {
        private readonly Queue<HttpStatusCode> _responses = [];
        private readonly List<string> _signatures = [];

        public int RequestCount { get; private set; }
        public IReadOnlyList<string> Signatures => _signatures;

        public void RespondWith(params HttpStatusCode[] responses)
        {
            foreach (var response in responses)
            {
                _responses.Enqueue(response);
            }
        }

        public void Reset()
        {
            RequestCount = 0;
            _responses.Clear();
            _signatures.Clear();
        }

        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestCount++;
            if (request.Headers.TryGetValues(
                    "X-NotifyRail-Signature",
                    out var signatures))
            {
                _signatures.AddRange(signatures);
            }
            var statusCode = _responses.TryDequeue(out var response)
                ? response
                : HttpStatusCode.NoContent;
            return Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(RemoteResponseBody),
            });
        }
    }
}
