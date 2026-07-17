namespace NotifyRail.Api.Features.Deliveries.ProviderCallbacks.Mock;

public sealed class MockProviderCallbackOptions
{
    public const string SectionName = "MockProviderCallback";

    public string Secret { get; set; } = string.Empty;

    public TimeSpan SignatureTolerance { get; set; } = TimeSpan.FromMinutes(5);
}
