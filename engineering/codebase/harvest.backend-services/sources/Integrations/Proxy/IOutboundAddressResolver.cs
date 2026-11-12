using System.Net;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Proxy;

/// <summary>Resolves destinations for explicit proxy validation without coupling tests to live DNS.</summary>
public interface IOutboundAddressResolver
{
    /// <summary>Resolves the host's current address set.</summary>
    Task<IPAddress[]> ResolveAsync(string host, CancellationToken cancellationToken = default);
}
