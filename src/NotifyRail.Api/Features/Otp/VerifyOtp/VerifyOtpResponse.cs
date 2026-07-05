using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.Otp.VerifyOtp;

public sealed record VerifyOtpResponse(
    [property: JsonPropertyName("otp_id")]
    Guid OtpId,

    [property: JsonPropertyName("status")]
    string Status,

    [property: JsonPropertyName("verified_at")]
    DateTimeOffset VerifiedAt);
