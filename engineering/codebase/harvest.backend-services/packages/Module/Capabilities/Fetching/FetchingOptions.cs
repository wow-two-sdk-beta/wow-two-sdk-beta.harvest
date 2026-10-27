namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities.Fetching;

/// <summary>Holds how Harvest fetches: the honest user agent an operator sets, the direct-connection safety toggles, the
/// size and time limits of one fetch, and the per-host pacing defaults.</summary>
/// <remarks>Direct, polite fetching only. These settings slow a fetch, cap it or name it; none of them works around a
/// block, a challenge or a rate limit, and there is no proxy, rotation or disguised profile to configure.</remarks>
public sealed class FetchingOptions
{
    /// <summary>The user agent Harvest sends when the operator sets none — plain and honest, naming Harvest and pointing
    /// at the public repository so a host operator can see who is fetching.</summary>
    public const string DefaultUserAgent = "HarvestBot/1.0 (+https://github.com/wow-two-sdk-beta/wow-two-sdk-beta.harvest)";

    /// <summary>Gets or sets the user agent sent on every fetch; an operator sets their own, naming the deployment and a
    /// way to reach them. It is never disguised to look like a browser.</summary>
    public string UserAgent { get; set; } = DefaultUserAgent;

    /// <summary>Gets or sets whether a fetch target must use HTTPS; the open web includes plain HTTP, so this defaults to
    /// off.</summary>
    public bool RequireHttps { get; set; }

    /// <summary>Gets or sets whether a fetch may connect to a private or loopback address; off by default so a redirect
    /// cannot steer a fetch at an internal service. Tests against a local fixture host turn it on.</summary>
    public bool AllowPrivateNetworkTargets { get; set; }

    /// <summary>Gets or sets how long one connection may take to open.</summary>
    public TimeSpan ConnectTimeout { get; set; } = TimeSpan.FromSeconds(15);

    /// <summary>Gets or sets how long one fetch may take in all.</summary>
    public TimeSpan Timeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>Gets or sets the most bytes a fetched body may hold before the fetch is abandoned as too large.</summary>
    public long MaxResponseBytes { get; set; } = 8L * 1024 * 1024;

    /// <summary>Gets or sets the most bytes a <c>robots.txt</c> may hold before the rest is ignored.</summary>
    public long RobotsMaxBytes { get; set; } = 512L * 1024;

    /// <summary>Gets or sets how many redirects a fetch follows before it gives up as an invalid redirect.</summary>
    public int MaxRedirects { get; set; } = 5;

    /// <summary>Gets or sets how long a host's cached <c>robots.txt</c> stands before it is fetched again.</summary>
    public TimeSpan RobotsCacheTtl { get; set; } = TimeSpan.FromHours(24);

    /// <summary>Gets or sets the gap between fetches of one host when nothing else sets a longer one; pacing never lets it
    /// fall below the 2 s floor.</summary>
    public TimeSpan MinDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Gets or sets the longest a host's adaptive delay may widen to as fetches keep failing.</summary>
    public TimeSpan MaxDelay { get; set; } = TimeSpan.FromMinutes(5);
}
