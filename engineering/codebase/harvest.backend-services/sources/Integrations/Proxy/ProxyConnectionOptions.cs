namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Proxy;

/// <summary>Holds a fixed proxy endpoint and optional credentials from a private host file or vault.</summary>
public sealed class ProxyConnectionOptions
{
    /// <summary>Gets or sets an HTTP, HTTPS, SOCKS4, SOCKS4a or SOCKS5 proxy URL without embedded credentials.</summary>
    public string Server { get; set; } = string.Empty;
    /// <summary>Gets or sets the proxy username; this is never a source-site header.</summary>
    public string? Username { get; set; }
    /// <summary>Gets or sets the proxy password; never store it in a checked-in configuration file.</summary>
    public string? Password { get; set; }
}
