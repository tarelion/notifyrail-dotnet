namespace NotifyRail.Api.Authentication;

public sealed class OperatorAuthenticationOptions
{
    public const string SectionName = "Authentication:Operator";

    public string Credential { get; init; } = string.Empty;
}
