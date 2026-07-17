using System.Security.Cryptography;
using System.Text;

namespace NotifyRail.Api.Features.ApiClients;

public sealed record ApiKeyCredential(
    string Plaintext,
    string LookupId,
    byte[] VerificationHash,
    string DisplayPrefix)
{
    private const string Prefix = "nrk";

    public static ApiKeyCredential Generate()
    {
        var lookupId = Convert.ToHexStringLower(RandomNumberGenerator.GetBytes(16));
        var secret = Base64UrlEncode(RandomNumberGenerator.GetBytes(32));
        var plaintext = $"{Prefix}_{lookupId}_{secret}";

        return new ApiKeyCredential(
            plaintext,
            lookupId,
            Hash(secret),
            plaintext[..16]);
    }

    public static bool TryParse(
        string plaintext,
        out string lookupId,
        out byte[] verificationHash)
    {
        lookupId = string.Empty;
        verificationHash = [];

        var parts = plaintext.Split('_', 3);
        if (parts.Length != 3
            || parts[0] != Prefix
            || parts[1].Length == 0
            || parts[2].Length == 0)
        {
            return false;
        }

        lookupId = parts[1];
        verificationHash = Hash(parts[2]);
        return true;
    }

    private static byte[] Hash(string secret)
    {
        return SHA256.HashData(Encoding.UTF8.GetBytes(secret));
    }

    private static string Base64UrlEncode(byte[] bytes)
    {
        return Convert.ToBase64String(bytes)
            .TrimEnd('=')
            .Replace('+', '-')
            .Replace('/', '_');
    }
}
