using Microsoft.AspNetCore.Http;

namespace NotifyRail.Api.Features.Deliveries.ProviderCallbacks;

public interface IProviderCallbackVerifier
{
    bool IsAuthentic(
        IHeaderDictionary headers,
        ReadOnlySpan<byte> body);
}
