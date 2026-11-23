using WoW.Two.Sdk.Beta.Be.Http.Safety;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Proxy;

/// <summary>Creates a proxy client restricted to one source origin.</summary>
public interface IProxyHttpClientFactory
{
    /// <summary>Gets whether both client creation methods support closing an unread response.</summary>
    bool SupportsEarlyBodyStop => false;

    /// <summary>Creates a caller-owned, redirect-free client with explicit proxy credentials.</summary>
    HttpClient CreateClient(Uri sourceOrigin, ProxyConnectionOptions proxy);
    /// <summary>Creates a caller-owned web-read client restricted to an acquired proxy lease.</summary>
    HttpClient CreateClient(OutboundProxyLease lease) => lease.CreateWebReadClient();

    /// <summary>Creates a guarded client with response draining disabled when requested and supported.</summary>
    HttpClient CreateClient(Uri sourceOrigin, ProxyConnectionOptions proxy, bool allowPrefix) => CreateClient(sourceOrigin, proxy);

    /// <summary>Creates a lease-bound client with response draining disabled when requested and supported.</summary>
    HttpClient CreateClient(OutboundProxyLease lease, bool allowPrefix) => CreateClient(lease);
}
