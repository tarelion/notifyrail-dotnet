using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.Otp.SendOtp;

public sealed record SendOtpResponse(
    [property: JsonPropertyName("otp_id")]
    Guid OtpId,

    [property: JsonPropertyName("message_id")]
    Guid MessageId,

    [property: JsonPropertyName("expires_at")]
    DateTimeOffset ExpiresAt,

    [property: JsonPropertyName("debug_code")]
    string DebugCode);
