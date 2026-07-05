namespace NotifyRail.Api.Features.Otp;

public sealed class OtpOptions
{
    public const string SectionName = "Otp";

    public string SenderTitle { get; set; } = "NotifyRail";

    public string Secret { get; set; } = string.Empty;

    public TimeSpan Ttl { get; set; } = TimeSpan.FromMinutes(5);

    public int MaxAttempts { get; set; } = 5;
}
