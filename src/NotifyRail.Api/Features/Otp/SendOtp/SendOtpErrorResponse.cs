using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.Otp.SendOtp;

public sealed record SendOtpErrorResponse(
    [property: JsonPropertyName("error")]
    string Error);
