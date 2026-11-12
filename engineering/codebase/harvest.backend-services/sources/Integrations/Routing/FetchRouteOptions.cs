using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Proxy;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Routing;

/// <summary>Binds one source origin to one route and optional account-session pool.</summary>
public sealed class FetchRouteOptions
{
    /// <summary>Gets or sets a non-secret route identifier for diagnostics.</summary>
    public string Key { get; set; } = string.Empty;
    /// <summary>Gets or sets the exact HTTP(S) origin, including a non-default port when used.</summary>
    public string Origin { get; set; } = string.Empty;
    /// <summary>Gets or sets the route; direct is the default.</summary>
    public FetchRouteMode Mode { get; set; }
    /// <summary>Gets or sets the fixed proxy settings.</summary>
    public ProxyConnectionOptions? Proxy { get; set; }
    /// <summary>Gets or sets the configured profile name for proxy mode.</summary>
    public string? ProxyProfile { get; set; }
    /// <summary>Gets or sets the IP-lease broker settings.</summary>
    public IpLeaseBrokerOptions? IpLease { get; set; }
    /// <summary>Gets the session IDs allowed for this route; empty means no account cookies.</summary>
    public List<string> SessionIds { get; set; } = [];
}
