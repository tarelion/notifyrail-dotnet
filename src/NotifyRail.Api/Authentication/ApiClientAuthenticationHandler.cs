using System.Security.Claims;
using System.Security.Cryptography;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NotifyRail.Api.Features.ApiClients;
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
        if (!TryReadCredential(out var lookupId, out var verificationHash))
        {
            return AuthenticateResult.NoResult();
        }

        var authenticatedAt = timeProvider.GetUtcNow();

        var apiKey = await dbContext.ApiKeys
            .SingleOrDefaultAsync(candidate => candidate.LookupId == lookupId, Context.RequestAborted);
        if (apiKey is null
            || apiKey.RevokedAt is not null
            || apiKey.ExpiresAt <= authenticatedAt
            || !CryptographicOperations.FixedTimeEquals(
                apiKey.VerificationHash,
                verificationHash))
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

        apiKey.RecordUse(authenticatedAt);
        await dbContext.SaveChangesAsync(Context.RequestAborted);

        var identity = new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, apiClient.Id.ToString()),
                new Claim("notifyrail:api_client_id", apiClient.Id.ToString()),
            ],
            SchemeName);
        var principal = new ClaimsPrincipal(identity);

        return AuthenticateResult.Success(new AuthenticationTicket(principal, SchemeName));
    }

    private bool TryReadCredential(out string lookupId, out byte[] verificationHash)
    {
        lookupId = string.Empty;
        verificationHash = [];

        if (!Request.Headers.TryGetValue("Authorization", out var values))
        {
            return false;
        }

        var authorization = values.ToString();
        if (!authorization.StartsWith(AuthorizationPrefix, StringComparison.Ordinal))
        {
            return false;
        }

        return ApiKeyCredential.TryParse(
            authorization[AuthorizationPrefix.Length..],
            out lookupId,
            out verificationHash);
    }
}
