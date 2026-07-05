using System.Text.Json.Serialization;

namespace NotifyRail.Api.Features.Otp.SendOtp;

public sealed record SendOtpRequest(
    [property: JsonPropertyName("recipient")]
    string? Recipient,

    [property: JsonPropertyName("idempotency_key")]
    string? IdempotencyKey);
