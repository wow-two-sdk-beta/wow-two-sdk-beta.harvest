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
    /// <summary>Checks each robots, content and redirect URL before the client receives it.</summary>
    bool Allows(Uri destination);
}
