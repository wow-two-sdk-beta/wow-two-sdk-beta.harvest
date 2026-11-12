namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Routing;

/// <summary>Identifies one explicitly chosen route; refusals never select another route automatically.</summary>
public enum FetchRouteMode
{
    /// <summary>Connect directly through the public engine's guarded transport.</summary>
    Direct,
    /// <summary>Use a fixed operator-configured HTTP or SOCKS proxy.</summary>
    Proxy,
    /// <summary>Acquire one proxy from an explicitly configured IP-lease broker.</summary>
    IpLease,
}
