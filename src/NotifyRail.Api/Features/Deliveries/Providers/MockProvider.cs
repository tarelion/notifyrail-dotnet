using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;
using NotifyRail.Api.Features.Deliveries.Queue;

namespace NotifyRail.Api.Features.Deliveries.Providers;

public sealed class MockProvider : IProviderSender
{
    private readonly IReadOnlyDictionary<string, ProviderOutcome[]> _recipientOutcomes;

    public MockProvider()
        : this(Options.Create(new MockProviderOptions()))
    {
    }

    public MockProvider(IOptions<MockProviderOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        var rules = options.Value.Rules;
        if (rules.Any(rule => rule.Outcomes.Count == 0))
        {
            throw new OptionsValidationException(
                MockProviderOptions.SectionName,
                typeof(MockProviderOptions),
                ["must define at least one outcome"]);
        }

        _recipientOutcomes = rules.ToDictionary(
            rule => rule.Recipient.Trim(),
            rule => rule.Outcomes.Select(ParseOutcome).ToArray(),
            StringComparer.Ordinal);
    }

    public string Name => "mock";

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

        var outcome = ConfiguredOutcome(request);
        if (outcome == ProviderOutcome.RetryableFailure)
        {
            return Task.FromResult(new ProviderResult(
                outcome,
                Provider: Name,
                ErrorCode: "mock_retryable_failure",
                ErrorMessage: "Mock provider returned a retryable failure."));
        }
        if (outcome == ProviderOutcome.PermanentFailure)
        {
            return Task.FromResult(new ProviderResult(
                outcome,
                Provider: Name,
                ErrorCode: "mock_permanent_failure",
                ErrorMessage: "Mock provider returned a permanent failure."));
        }

        return Task.FromResult(new ProviderResult(
            ProviderOutcome.Accepted,
            Provider: Name,
            ProviderMessageId: CreateProviderMessageId(request.IdempotencyKey)));
    }

    private ProviderOutcome ConfiguredOutcome(ProviderRequest request)
    {
        if (!_recipientOutcomes.TryGetValue(request.Recipient, out var outcomes) ||
            outcomes.Length == 0)
        {
            return ProviderOutcome.Accepted;
        }

        var outcomeIndex = Math.Min(request.AttemptNumber - 1, outcomes.Length - 1);
        return outcomes[outcomeIndex];
    }

    private static ProviderOutcome ParseOutcome(string outcome)
    {
        return outcome.Trim() switch
        {
            "accepted" => ProviderOutcome.Accepted,
            "retryable_failure" => ProviderOutcome.RetryableFailure,
            "permanent_failure" => ProviderOutcome.PermanentFailure,
            _ => throw new ArgumentException($"Unknown mock provider outcome: {outcome}"),
        };
    }

    private static string CreateProviderMessageId(string idempotencyKey)
    {
        var digest = SHA256.HashData(Encoding.UTF8.GetBytes(idempotencyKey));
        return "mock_" + Convert.ToHexString(digest).ToLowerInvariant();
    }
}
