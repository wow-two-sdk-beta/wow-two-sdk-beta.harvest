using System.Net;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Http.Safety;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Proxy;

/// <summary>Builds the SDK's public-address proxy transport for a Harvest source.</summary>
public sealed class GuardedProxyClientFactory(IOutboundAddressResolver resolver) : IProxyHttpClientFactory
{
    /// <inheritdoc />
    public HttpClient CreateClient(OutboundProxyLease lease) => lease.CreateWebReadClient(resolver.ResolveAsync);

    /// <inheritdoc />
    public HttpClient CreateClient(Uri sourceOrigin, ProxyConnectionOptions proxy)
    {
        if (!IntegrationValidation.IsWeb(sourceOrigin) || !SafeOutboundProxy.TryParseEndpoint(proxy.Server, out Uri? endpoint))
            throw new FetchTransportUnavailableException("The configured proxy endpoint is invalid.");

        var credentials = string.IsNullOrEmpty(proxy.Username) ? null : new NetworkCredential(proxy.Username, proxy.Password);
        return SafeOutboundProxy.CreateClient(sourceOrigin, endpoint, credentials, resolver.ResolveAsync);
    }
}
