using System.Net;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Http.Safety;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Proxy;

/// <summary>Builds the SDK's public-address proxy transport for a Harvest source.</summary>
public sealed class GuardedProxyClientFactory(IOutboundAddressResolver resolver) : IProxyHttpClientFactory
{
    /// <inheritdoc />
    public bool SupportsEarlyBodyStop => true;

    /// <inheritdoc />
    public HttpClient CreateClient(OutboundProxyLease lease) => lease.CreateWebReadClient(resolver.ResolveAsync);

    /// <inheritdoc />
    public HttpClient CreateClient(OutboundProxyLease lease, bool allowPrefix) => allowPrefix
        ? CreatePrefixClient(lease.CreateHandler(OutboundProxyTraffic.WebRead, resolver.ResolveAsync))
        : CreateClient(lease);

    /// <inheritdoc />
    public HttpClient CreateClient(Uri sourceOrigin, ProxyConnectionOptions proxy) => CreateClient(sourceOrigin, proxy, allowPrefix: false);

    /// <inheritdoc />
    public HttpClient CreateClient(Uri sourceOrigin, ProxyConnectionOptions proxy, bool allowPrefix)
    {
        if (!IntegrationValidation.IsWeb(sourceOrigin) || !SafeOutboundProxy.TryParseEndpoint(proxy.Server, out Uri? endpoint))
            throw new FetchTransportUnavailableException("The configured proxy endpoint is invalid.");

        var credentials = string.IsNullOrEmpty(proxy.Username) ? null : new NetworkCredential(proxy.Username, proxy.Password);
        return allowPrefix
            ? CreatePrefixClient(SafeOutboundProxy.CreateHandler(sourceOrigin, endpoint, OutboundProxyTraffic.WebRead, credentials, resolver.ResolveAsync))
            : SafeOutboundProxy.CreateClient(sourceOrigin, endpoint, credentials, resolver.ResolveAsync);
    }

    private static HttpClient CreatePrefixClient(HttpMessageHandler handler)
    {
        if (handler is not DelegatingHandler { InnerHandler: SocketsHttpHandler sockets })
        {
            handler.Dispose();
            throw new FetchTransportUnavailableException("The guarded proxy transport cannot disable response draining.");
        }

        sockets.MaxResponseDrainSize = 0;
        sockets.ResponseDrainTimeout = TimeSpan.Zero;
        return new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan };
    }
}
