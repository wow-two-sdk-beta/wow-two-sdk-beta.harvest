using System.Net;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;

/// <summary>Creates the HTTP clients Harvest fetches with: direct from the host's own address, through core's safe
/// outbound client, under Harvest's one configured user agent.</summary>
/// <remarks>There is no other route — no proxy, no rotation, no disguised client. Generic retries stay off: a challenge, a
/// block or a rate limit is an outcome that backs the host off, never something to retry past.</remarks>
public interface IFetchClientFactory
{
    /// <summary>Creates a direct client that carries the given cookie jar.</summary>
    /// <param name="cookies">The run's anonymous cookie jar, or none.</param>
    /// <returns>A client the caller disposes.</returns>
    HttpClient CreateClient(CookieContainer? cookies = null);
}
