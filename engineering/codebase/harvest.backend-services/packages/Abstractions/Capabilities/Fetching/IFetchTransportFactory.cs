using System.Net;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;

/// <summary>Acquires one explicitly configured network route after the host's permission and pause gates.</summary>
public interface IFetchTransportFactory
{
    /// <summary>Acquires a route for one page read, including its robots and approved redirects.</summary>
    /// <param name="destination">The initial public page URL.</param>
    /// <param name="runCookies">The caller's anonymous cookie jar, when supplied.</param>
    /// <param name="cancellationToken">The token that cancels acquisition.</param>
    ValueTask<IFetchTransportLease> AcquireAsync(
        Uri destination,
        CookieContainer? runCookies = null,
        CancellationToken cancellationToken = default);
}
