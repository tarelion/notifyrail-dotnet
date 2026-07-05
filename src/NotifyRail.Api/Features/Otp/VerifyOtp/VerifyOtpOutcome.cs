namespace NotifyRail.Api.Features.Otp.VerifyOtp;

public sealed record VerifyOtpOutcome(
    VerifyOtpOutcomeKind Kind,
    VerifyOtpResponse? Response,
    int? AttemptsRemaining = null);

public enum VerifyOtpOutcomeKind
{
    Verified,
    NotFound,
    AlreadyVerified,
    Expired,
    AttemptLimitExceeded,
    IncorrectCode,
}
