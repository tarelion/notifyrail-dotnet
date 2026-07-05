namespace NotifyRail.Api.Features.Deliveries.Providers;

public sealed class MockProviderOptions
{
    public const string SectionName = "MockProvider";

    public List<MockProviderRule> Rules { get; set; } = [];
}

public sealed class MockProviderRule
{
    public string Recipient { get; set; } = string.Empty;

    public List<string> Outcomes { get; set; } = [];
}
