using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.Otp.VerifyOtp;

public sealed record VerifyOtpRequest(
    [property: JsonPropertyName("otp_id")]
    Guid? OtpId,

    [property: JsonPropertyName("code")]
    string? Code);
