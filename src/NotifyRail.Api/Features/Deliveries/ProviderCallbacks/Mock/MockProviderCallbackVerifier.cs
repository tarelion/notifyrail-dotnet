using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace NotifyRail.Api.Features.Deliveries.ProviderCallbacks.Mock;

public sealed class MockProviderCallbackVerifier(
    IOptions<MockProviderCallbackOptions> options,
    TimeProvider timeProvider) : IProviderCallbackVerifier
{
    public bool IsAuthentic(
        IHeaderDictionary headers,
        ReadOnlySpan<byte> body)
    {
        if (!headers.TryGetValue("X-Mock-Provider-Timestamp", out var timestamps) ||
            timestamps.Count != 1 ||
            !headers.TryGetValue("X-Mock-Provider-Signature", out var signatures) ||
            signatures.Count != 1)
        {
            return false;
        }

        var timestamp = timestamps[0]!;
        var signature = signatures[0]!;
        if (!long.TryParse(
                timestamp,
                NumberStyles.None,
                CultureInfo.InvariantCulture,
                out var unixTimestamp))
        {
            return false;
        }

        DateTimeOffset signedAt;
        try
        {
            signedAt = DateTimeOffset.FromUnixTimeSeconds(unixTimestamp);
        }
        catch (ArgumentOutOfRangeException)
        {
            return false;
        }

        var clockDifference = timeProvider.GetUtcNow() - signedAt;
        if (clockDifference < -options.Value.SignatureTolerance ||
            clockDifference > options.Value.SignatureTolerance)
        {
            return false;
        }

        const string versionPrefix = "v1=";
        if (!signature.StartsWith(versionPrefix, StringComparison.Ordinal) ||
            signature.Length != versionPrefix.Length + 64)
        {
            return false;
        }

        byte[] suppliedSignature;
        try
        {
            suppliedSignature = Convert.FromHexString(
                signature.AsSpan(versionPrefix.Length));
        }
        catch (FormatException)
        {
            return false;
        }

        using var hmac = IncrementalHash.CreateHMAC(
            HashAlgorithmName.SHA256,
            Encoding.UTF8.GetBytes(options.Value.Secret));
        hmac.AppendData(Encoding.UTF8.GetBytes($"{timestamp}."));
        hmac.AppendData(body);
        Span<byte> expectedSignature = stackalloc byte[32];
        if (!hmac.TryGetHashAndReset(expectedSignature, out var bytesWritten) ||
            bytesWritten != expectedSignature.Length)
        {
            return false;
        }

        return CryptographicOperations.FixedTimeEquals(
            suppliedSignature,
            expectedSignature);
    }
}
