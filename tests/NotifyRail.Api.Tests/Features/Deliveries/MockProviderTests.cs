using Microsoft.Extensions.Options;
using NotifyRail.Api.Features.Deliveries.Providers;
using NotifyRail.Api.Features.Deliveries.Queue;

namespace NotifyRail.Api.Tests;

public sealed class MockProviderTests
{
    [Fact]
    public async Task SendAsync_AcceptsMessageWithStableProviderMessageId()
    {
        var provider = new MockProvider();
        var request = CreateRequest("delivery-1-attempt-1");

        var first = await provider.SendAsync(request, CancellationToken.None);
        var replay = await provider.SendAsync(request, CancellationToken.None);
        var second = await provider.SendAsync(
            CreateRequest("delivery-1-attempt-2"),
            CancellationToken.None);

        Assert.Equal(ProviderOutcome.Accepted, first.Outcome);
        Assert.Equal("mock", first.Provider);
        Assert.NotNull(first.ProviderMessageId);
        Assert.StartsWith("mock_", first.ProviderMessageId);
        Assert.Equal(first.ProviderMessageId, replay.ProviderMessageId);
        Assert.NotEqual(first.ProviderMessageId, second.ProviderMessageId);
    }

    [Fact]
    public async Task SendAsync_FollowsConfiguredRecipientOutcomeSequence()
    {
        var provider = new MockProvider(Options.Create(new MockProviderOptions
        {
            Rules =
            [
                new MockProviderRule
                {
                    Recipient = "+905551111111",
                    Outcomes = ["retryable_failure", "accepted"],
                },
            ],
        }));

        var first = await provider.SendAsync(
            CreateRequest("delivery-1-attempt-1", attemptNumber: 1),
            CancellationToken.None);
        var second = await provider.SendAsync(
            CreateRequest("delivery-1-attempt-2", attemptNumber: 2),
            CancellationToken.None);

        Assert.Equal(ProviderOutcome.RetryableFailure, first.Outcome);
        Assert.Equal("mock", first.Provider);
        Assert.Null(first.ProviderMessageId);
        Assert.Equal("mock_retryable_failure", first.ErrorCode);
        Assert.Equal("Mock provider returned a retryable failure.", first.ErrorMessage);

        Assert.Equal(ProviderOutcome.Accepted, second.Outcome);
        Assert.NotNull(second.ProviderMessageId);
        Assert.StartsWith("mock_", second.ProviderMessageId);
        Assert.Null(second.ErrorCode);
        Assert.Null(second.ErrorMessage);
    }

    [Fact]
    public void Constructor_RejectsRuleWithoutOutcomes()
    {
        var options = Options.Create(new MockProviderOptions
        {
            Rules =
            [
                new MockProviderRule
                {
                    Recipient = "+905551111111",
                },
            ],
        });

        var exception = Assert.Throws<OptionsValidationException>(() =>
            new MockProvider(options));

        Assert.Contains(
            "must define at least one outcome",
            exception.Failures);
    }

    [Fact]
    public async Task SendAsync_RejectsMissingIdempotencyKey()
    {
        var provider = new MockProvider();

        var exception = await Assert.ThrowsAsync<ArgumentException>(() =>
            provider.SendAsync(CreateRequest("   "), CancellationToken.None));

        Assert.Equal("request", exception.ParamName);
    }

    [Fact]
    public async Task SendAsync_StopsWhenCancellationIsRequested()
    {
        var provider = new MockProvider();
        using var cancellation = new CancellationTokenSource();
        await cancellation.CancelAsync();

        await Assert.ThrowsAsync<OperationCanceledException>(() =>
            provider.SendAsync(
                CreateRequest("delivery-1-attempt-1"),
                cancellation.Token));
    }

    private static ProviderRequest CreateRequest(
        string idempotencyKey,
        int attemptNumber = 1)
    {
        return new ProviderRequest(
            idempotencyKey,
            Recipient: "+905551111111",
            Channel: "sms",
            SenderTitle: "NotifyRail",
            Body: "Your order is ready.",
            AttemptNumber: attemptNumber);
    }
}
