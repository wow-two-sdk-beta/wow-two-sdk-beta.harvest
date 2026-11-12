using System.Net;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Proxy;

/// <summary>Resolves proxy and source hosts through the host's normal DNS resolver.</summary>
internal sealed class SystemOutboundAddressResolver : IOutboundAddressResolver
{
    public Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken = default)
    {
        return Dns.GetHostAddressesAsync(host, cancellationToken);
    }
}
