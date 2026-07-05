using System.Globalization;
using System.Buffers.Binary;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Options;

namespace NotifyRail.Api.Features.Otp;

public sealed class OtpCode
{
    private readonly byte[] _secret;

    public OtpCode(IOptions<OtpOptions> options)
    {
        ArgumentNullException.ThrowIfNull(options);

        _secret = Encoding.UTF8.GetBytes(options.Value.Secret);
    }

    public string Derive(Guid challengeId)
    {
        var digest = HMACSHA256.HashData(
            _secret,
            Encoding.UTF8.GetBytes($"code:{challengeId:N}"));
        var value = BinaryPrimitives.ReadUInt32BigEndian(digest) % 1_000_000;
        return value.ToString("D6", CultureInfo.InvariantCulture);
    }

    public byte[] Hash(Guid challengeId, string code)
    {
        return HMACSHA256.HashData(
            _secret,
            Encoding.UTF8.GetBytes($"verify:{challengeId:N}:{code}"));
    }

    public bool Matches(Guid challengeId, string code, byte[] expectedHash)
    {
        return CryptographicOperations.FixedTimeEquals(
            Hash(challengeId, code),
            expectedHash);
    }
}
