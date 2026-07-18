namespace NotifyRail.Api.Features.Webhooks.Secrets;

public interface IWebhookSecretProtector
{
    byte[] Protect(string plaintext);

    string Unprotect(byte[] protectedValue);
}
