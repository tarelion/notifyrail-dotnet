namespace NotifyRail.Api.Features.Otp.SendOtp;

public sealed record SendOtpOutcome(
    SendOtpOutcomeKind Kind,
    SendOtpResponse? Response,
    string? Error)
{
    public static SendOtpOutcome Accepted(SendOtpResponse response)
    {
        return new SendOtpOutcome(SendOtpOutcomeKind.Accepted, response, null);
    }

    public static SendOtpOutcome IdempotencyConflict(string error)
    {
        return new SendOtpOutcome(
            SendOtpOutcomeKind.IdempotencyConflict,
            null,
            error);
    }
}

public enum SendOtpOutcomeKind
{
    Accepted,
    IdempotencyConflict,
}
