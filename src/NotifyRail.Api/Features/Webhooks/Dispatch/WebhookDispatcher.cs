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
        using var response = await httpClient.SendAsync(
            message,
            HttpCompletionOption.ResponseHeadersRead,
            cancellationToken);
        stopwatch.Stop();
        var statusCode = (int)response.StatusCode;
        var succeeded = statusCode is >= 200 and <= 299;
        return new WebhookResult(
            succeeded,
            statusCode,
            stopwatch.ElapsedMilliseconds,
            succeeded ? null : "http_error",
            succeeded ? null : $"Webhook endpoint returned HTTP {statusCode}.");
    }

    private static string Sign(string secret, string timestamp, string body)
    {
        var content = Encoding.UTF8.GetBytes($"{timestamp}.{body}");
        var hash = HMACSHA256.HashData(Encoding.UTF8.GetBytes(secret), content);
        return $"v1={Convert.ToHexStringLower(hash)}";
    }
}
