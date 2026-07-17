using System.Security.Cryptography;

namespace NotifyRail.Api.Features.ApiClients.CreateApiClient;

public sealed record ApiKeyCredential(
    string Plaintext,
    string LookupId,
    byte[] VerificationHash,
    string DisplayPrefix)
{
    public static ApiKeyCredential Generate()
    {
        var lookupId = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var secret = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var plaintext = $"nrk_{lookupId}_{secret}";

        return new ApiKeyCredential(
            plaintext,
            lookupId,
            SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(secret)),
            plaintext[..16]);
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
