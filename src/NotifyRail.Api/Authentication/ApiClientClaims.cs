using System.Security.Claims;

namespace NotifyRail.Api.Authentication;

public static class ApiClientClaims
{
    public static bool TryGetApiClientId(
        ClaimsPrincipal principal,
        out Guid apiClientId)
    {
        return Guid.TryParse(
            principal.FindFirstValue(ClaimTypes.NameIdentifier),
            out apiClientId);
    }
}
