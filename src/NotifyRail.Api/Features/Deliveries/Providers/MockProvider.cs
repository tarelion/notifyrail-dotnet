using System.Security.Cryptography;
using System.Text;
using NotifyRail.Api.Features.Deliveries.Queue;

namespace NotifyRail.Api.Features.Deliveries.Providers;

public sealed class MockProvider : IProviderSender
{
    public Task<ProviderResult> SendAsync(
        ProviderRequest request,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(request);

        cancellationToken.ThrowIfCancellationRequested();

        if (string.IsNullOrWhiteSpace(request.IdempotencyKey))
        {
            throw new ArgumentException(
                "Idempotency key is required.",
                nameof(request));
        }

        var digest = SHA256.HashData(
            Encoding.UTF8.GetBytes(request.IdempotencyKey));
        var providerMessageId = "mock_" +
            Convert.ToHexString(digest).ToLowerInvariant();

        return Task.FromResult(
            new ProviderResult(
                ProviderOutcome.Accepted,
                Provider: "mock",
                ProviderMessageId: providerMessageId));
    }
}
