using System.Security.Cryptography;
using Microsoft.AspNetCore.WebUtilities;

namespace NotifyRail.Api.Features.Webhooks.Secrets;

public static class WebhookSecretCredential
{
    public static string Generate()
    {
        return $"nrs_{WebEncoders.Base64UrlEncode(RandomNumberGenerator.GetBytes(32))}";
    }
}
