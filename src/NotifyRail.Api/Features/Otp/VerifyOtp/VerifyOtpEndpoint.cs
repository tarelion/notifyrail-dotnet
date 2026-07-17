using System.Security.Claims;
using NotifyRail.Api.Authentication;

namespace NotifyRail.Api.Features.Otp.VerifyOtp;

public static class VerifyOtpEndpoint
{
    public static IEndpointRouteBuilder MapVerifyOtpEndpoint(
        this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/otp/verify", VerifyAsync)
            .RequireAuthorization(AuthenticationPolicies.ApiClient)
            .WithName("VerifyOtp")
            .Produces<VerifyOtpResponse>(StatusCodes.Status200OK)
            .Produces<VerifyOtpErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<VerifyOtpErrorResponse>(StatusCodes.Status404NotFound)
            .Produces<VerifyOtpErrorResponse>(StatusCodes.Status409Conflict)
            .Produces<VerifyOtpErrorResponse>(StatusCodes.Status410Gone)
            .Produces<VerifyOtpErrorResponse>(StatusCodes.Status429TooManyRequests)
            .Produces(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static async Task<IResult> VerifyAsync(
        VerifyOtpRequest request,
        ClaimsPrincipal principal,
        OtpVerifier verifier,
        CancellationToken cancellationToken)
    {
        if (!ApiClientClaims.TryGetApiClientId(principal, out var apiClientId))
        {
            return Results.Unauthorized();
        }

        var code = request.Code?.Trim();
        if (request.OtpId is null || request.OtpId == Guid.Empty)
        {
            return Results.BadRequest(new VerifyOtpErrorResponse("otp_id is required"));
        }
        if (code is null || code.Length != 6 || code.Any(character => !char.IsAsciiDigit(character)))
        {
            return Results.BadRequest(
                new VerifyOtpErrorResponse("code must contain exactly 6 digits"));
        }

        var outcome = await verifier.VerifyAsync(
            apiClientId,
            request.OtpId.Value,
            code,
            cancellationToken);

        return outcome.Kind switch
        {
            VerifyOtpOutcomeKind.Verified => Results.Ok(outcome.Response),
            VerifyOtpOutcomeKind.NotFound => Results.NotFound(
                new VerifyOtpErrorResponse("OTP challenge not found")),
            VerifyOtpOutcomeKind.AlreadyVerified => Results.Conflict(
                new VerifyOtpErrorResponse("OTP challenge is already verified")),
            VerifyOtpOutcomeKind.Expired => Results.Json(
                new VerifyOtpErrorResponse("OTP challenge has expired"),
                statusCode: StatusCodes.Status410Gone),
            VerifyOtpOutcomeKind.AttemptLimitExceeded => Results.Json(
                new VerifyOtpErrorResponse(
                    "OTP attempt limit exceeded",
                    outcome.AttemptsRemaining),
                statusCode: StatusCodes.Status429TooManyRequests),
            VerifyOtpOutcomeKind.IncorrectCode => Results.BadRequest(
                new VerifyOtpErrorResponse(
                    "invalid OTP code",
                    outcome.AttemptsRemaining)),
            _ => throw new InvalidOperationException(
                $"Unknown verify-OTP outcome: {outcome.Kind}"),
        };
    }
}
