using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.Otp.VerifyOtp;

public sealed record VerifyOtpErrorResponse(
    [property: JsonPropertyName("error")]
    string Error,

    [property: JsonPropertyName("attempts_remaining")]
    [property: JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    int? AttemptsRemaining = null);
