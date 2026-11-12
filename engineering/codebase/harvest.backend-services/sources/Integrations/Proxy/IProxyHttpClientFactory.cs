using WoW.Two.Sdk.Beta.Be.Http.Safety;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Proxy;

/// <summary>Creates a proxy client restricted to one source origin.</summary>
public interface IProxyHttpClientFactory
{
    /// <summary>Creates a caller-owned, redirect-free client with explicit proxy credentials.</summary>
    HttpClient CreateClient(Uri sourceOrigin, ProxyConnectionOptions proxy);
    /// <summary>Creates a caller-owned web-read client restricted to an acquired proxy lease.</summary>
    HttpClient CreateClient(OutboundProxyLease lease) => lease.CreateWebReadClient();
}
