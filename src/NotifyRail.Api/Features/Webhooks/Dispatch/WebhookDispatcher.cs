using System.Diagnostics;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using NotifyRail.Api.Features.Webhooks.Queue;
using NotifyRail.Api.Features.Webhooks.Secrets;

namespace NotifyRail.Api.Features.Webhooks.Dispatch;

public sealed class WebhookDispatcher(
    HttpClient httpClient,
    IWebhookSecretProtector secretProtector)
{
    public async Task<WebhookResult> SendAsync(
        WebhookRequest request,
        DateTimeOffset timestamp,
        CancellationToken cancellationToken)
    {
        var timestampValue = timestamp.ToUnixTimeSeconds().ToString();
        var secret = secretProtector.Unprotect(request.ProtectedSecret);
        var signature = Sign(secret, timestampValue, request.Body);
        using var message = new HttpRequestMessage(HttpMethod.Post, request.EndpointUrl);
        message.Headers.Add("X-NotifyRail-Event-Id", request.EventId.ToString());
        message.Headers.Add("X-NotifyRail-Timestamp", timestampValue);
        message.Headers.Add("X-NotifyRail-Signature", signature);
        message.Content = new StringContent(request.Body, Encoding.UTF8);
        message.Content.Headers.ContentType = new MediaTypeHeaderValue("application/json")
        {
            CharSet = "utf-8",
        };

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var response = await httpClient.SendAsync(
                message,
                HttpCompletionOption.ResponseHeadersRead,
                cancellationToken);
            stopwatch.Stop();
            var statusCode = (int)response.StatusCode;
            var outcome = statusCode switch
            {
                >= 200 and <= 299 => WebhookOutcome.Succeeded,
                408 or 429 or >= 500 => WebhookOutcome.RetryableFailure,
                _ => WebhookOutcome.PermanentFailure,
            };
            return new WebhookResult(
                outcome,
                statusCode,
                stopwatch.ElapsedMilliseconds,
                outcome == WebhookOutcome.Succeeded ? null : "http_error",
                outcome == WebhookOutcome.Succeeded ? null : $"Webhook endpoint returned HTTP {statusCode}.",
                outcome == WebhookOutcome.RetryableFailure
                    ? ParseRetryAfter(response)
                    : null);
        }
        catch (Exception exception) when (
            exception is HttpRequestException or TimeoutException or TaskCanceledException)
        {
            stopwatch.Stop();
            var requestCanceled = cancellationToken.IsCancellationRequested;
            var timedOut = !requestCanceled
                && exception is TimeoutException or TaskCanceledException;
            var errorCode = requestCanceled ? "request_canceled" : timedOut ? "timeout" : "network_error";
            var errorMessage = requestCanceled
                ? "Webhook request was canceled after dispatch started."
                : timedOut
                    ? "Webhook request timed out."
                    : "Webhook request failed before a response was received.";
            return new WebhookResult(
                WebhookOutcome.RetryableFailure,
                HttpStatusCode: null,
                stopwatch.ElapsedMilliseconds,
                errorCode,
                errorMessage);
        }
    }

    private static string Sign(string secret, string timestamp, string body)
    {
        var content = Encoding.UTF8.GetBytes($"{timestamp}.{body}");
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), content);
        return $"v1={Convert.ToHexStringLower(hash)}";
    }

    private static WebhookRetryAfter? ParseRetryAfter(HttpResponseMessage response)
    {
        if (!response.Headers.TryGetValues("Retry-After", out var values))
        {
            return null;
        }

        var value = values.FirstOrDefault();
        if (!RetryConditionHeaderValue.TryParse(value, out var retryAfter))
        {
            return null;
        }

        if (retryAfter.Delta is { } delta)
        {
            return new WebhookRetryAfter.Relative(delta);
        }

        return retryAfter.Date is { } date
            ? new WebhookRetryAfter.Absolute(date)
            : null;
    }
}
