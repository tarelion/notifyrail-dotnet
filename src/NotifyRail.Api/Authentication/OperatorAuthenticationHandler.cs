using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;

namespace NotifyRail.Api.Authentication;

public sealed class OperatorAuthenticationHandler(
    IOptionsMonitor<AuthenticationSchemeOptions> options,
    ILoggerFactory logger,
    UrlEncoder encoder,
    IOptionsMonitor<OperatorAuthenticationOptions> operatorOptions)
    : AuthenticationHandler<AuthenticationSchemeOptions>(options, logger, encoder)
{
    public const string SchemeName = "Operator";

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue("Authorization", out var values))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var authorization = values.ToString();
        const string prefix = SchemeName + " ";
        if (!authorization.StartsWith(prefix, StringComparison.Ordinal))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var suppliedCredential = authorization[prefix.Length..];
        if (!CredentialsMatch(suppliedCredential, operatorOptions.CurrentValue.Credential))
        {
            return Task.FromResult(AuthenticateResult.Fail("Invalid Operator credential."));
        }

        var identity = new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, "operator")],
            SchemeName);
        var principal = new ClaimsPrincipal(identity);
        var ticket = new AuthenticationTicket(principal, SchemeName);

        return Task.FromResult(AuthenticateResult.Success(ticket));
    }

    private static bool CredentialsMatch(string supplied, string expected)
    {
        var suppliedBytes = Encoding.UTF8.GetBytes(supplied);
        var expectedBytes = Encoding.UTF8.GetBytes(expected);

        return CryptographicOperations.FixedTimeEquals(suppliedBytes, expectedBytes);
    }
}
