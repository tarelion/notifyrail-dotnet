using System.Text;
using Microsoft.AspNetCore.DataProtection;

namespace NotifyRail.Api.Features.Webhooks.Secrets;

public sealed class DataProtectionWebhookSecretProtector : IWebhookSecretProtector
{
    private const string Purpose = "NotifyRail.Webhooks.Secrets.v1";
    private readonly IDataProtector _protector;

    public DataProtectionWebhookSecretProtector(IDataProtectionProvider provider)
    {
        _protector = provider.CreateProtector(Purpose);
    }

    public byte[] Protect(string plaintext)
    {
        return _protector.Protect(Encoding.UTF8.GetBytes(plaintext));
    }

    public string Unprotect(byte[] protectedValue)
    {
        return Encoding.UTF8.GetString(_protector.Unprotect(protectedValue));
    }
}
