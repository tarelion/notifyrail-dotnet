using System.Security.Claims;
using NotifyRail.Api.Authentication;

namespace NotifyRail.Api.Features.Otp.SendOtp;

public static class SendOtpEndpoint
{
    public static IEndpointRouteBuilder MapSendOtpEndpoint(this IEndpointRouteBuilder endpoints)
    {
        endpoints.MapPost("/otp/send", SendAsync)
            .RequireAuthorization(AuthenticationPolicies.ApiClient)
            .WithName("SendOtp")
            .Produces<SendOtpResponse>(StatusCodes.Status202Accepted)
            .Produces<SendOtpErrorResponse>(StatusCodes.Status400BadRequest)
            .Produces<SendOtpErrorResponse>(StatusCodes.Status409Conflict)
            .Produces(StatusCodes.Status401Unauthorized);

        return endpoints;
    }

    private static async Task<IResult> SendAsync(
        SendOtpRequest request,
        ClaimsPrincipal principal,
        OtpSender sender,
        CancellationToken cancellationToken)
    {
        if (!ApiClientClaims.TryGetApiClientId(principal, out var apiClientId))
        {
            return Results.Unauthorized();
        }

        var recipient = request.Recipient?.Trim();
        var idempotencyKey = request.IdempotencyKey?.Trim();
        if (string.IsNullOrWhiteSpace(recipient))
        {
            return Results.BadRequest(new SendOtpErrorResponse("recipient is required"));
        }
        if (string.IsNullOrWhiteSpace(idempotencyKey))
        {
            return Results.BadRequest(
                new SendOtpErrorResponse("idempotency_key is required"));
        }

        var outcome = await sender.SendAsync(
            apiClientId,
            recipient,
            idempotencyKey,
            cancellationToken);

        return outcome.Kind switch
        {
            SendOtpOutcomeKind.Accepted => Results.Accepted(value: outcome.Response),
            SendOtpOutcomeKind.IdempotencyConflict => Results.Conflict(
                new SendOtpErrorResponse(outcome.Error!)),
            _ => throw new InvalidOperationException(
                $"Unknown send-OTP outcome: {outcome.Kind}"),
        };
    }
}
