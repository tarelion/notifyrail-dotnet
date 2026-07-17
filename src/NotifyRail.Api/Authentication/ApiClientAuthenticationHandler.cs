using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NotifyRail.Api.Infrastructure.Persistence;

namespace NotifyRail.Api.Authentication;

public sealed class ApiClientAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    NotifyRailDbContext dbContext,
    TimeProvider timeProvider)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "ApiClient";
    private const string AuthorizationPrefix = "ApiKey ";

    protected override async Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!TryReadCredential(out var lookupId, out var secret))
        {
            return AuthenticateResult.NoResult();
        }

        var apiKey = await dbContext.ApiKeys
            .AsNoTracking()
            .SingleOrDefaultAsync(candidate => candidate.LookupId == lookupId, Context.RequestAborted);
        if (apiKey is null
            || apiKey.RevokedAt is not null
            || apiKey.ExpiresAt <= timeProvider.GetUtcNow()
            || !CryptographicOperations.FixedTimeEquals(
                apiKey.VerificationHash,
                SHA256.HashData(Encoding.UTF8.GetBytes(secret))))
        {
            return AuthenticateResult.Fail("Invalid API Key.");
        }

        var apiClient = await dbContext.ApiClients
            .AsNoTracking()
            .SingleOrDefaultAsync(
                candidate => candidate.Id == apiKey.ApiClientId,
                Context.RequestAborted);
        if (apiClient is null || !apiClient.IsEnabled)
        {
            return AuthenticateResult.Fail("Invalid API Key.");
        }

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, apiClient.Id.ToString()),
                new Claim("notifyrail:api_client_id", apiClient.Id.ToString()),
            ],
            SchemeName);
        var principal = new ClaimsPrincipal(identity);

        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    private bool TryReadCredential(out string lookupId, out string secret)
    {
        lookupId = string.Empty;
        secret = string.Empty;

        if (!Request.Headers.TryGetValue("Authorization", out var values))
        {
            return false;
        }

        var authorization = values.ToString();
        if (!authorization.StartsWith(AuthorizationPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        var credential = authorization[AuthorizationPrefix.Length..];
        var parts = credential.Split('_', 3);
        if (parts.Length != 3 || parts[0] != "nrk")
        {
            return false;
        }

        lookupId = parts[1];
        secret = parts[2];
        return lookupId.Length > 0 && secret.Length > 0;
    }
}
