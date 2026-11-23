using System.Net;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;

/// <summary>Holds an explicitly selected route and its isolated session for one fetch operation.</summary>
public interface IFetchTransportLease : IAsyncDisposable
{
    /// <summary>Gets the guarded client; automatic redirects and ambient cookies must be disabled.</summary>
    HttpClient Client { get; }
    /// <summary>Gets the route's cookie jar, or the caller's anonymous jar when appropriate.</summary>
    CookieContainer? Cookies { get; }
    /// <summary>Gets whether public conditional-response caching must be disabled.</summary>
    bool IsPrivate { get; }
    /// <summary>Gets a non-secret route identifier for diagnostics.</summary>
    string RouteKey { get; }
    /// <summary>Gets whether a host's <c>robots.txt</c> read through this route may be cached under (host, <see cref="RouteKey"/>)
    /// and reused by every later lease of the route: true only for a fixed exit with no account session and no caller jar, where
    /// the rules do not depend on who holds the lease. The default is false: a private route reads robots for each fetch.</summary>
    bool CachesRobotsByRoute => false;
    /// <summary>Gets whether disposing an unread response abandons the body without draining it.</summary>
    bool SupportsEarlyBodyStop => false;
    /// <summary>Checks each robots, content and redirect URL before the client receives it.</summary>
    bool Allows(Uri destination);
}
