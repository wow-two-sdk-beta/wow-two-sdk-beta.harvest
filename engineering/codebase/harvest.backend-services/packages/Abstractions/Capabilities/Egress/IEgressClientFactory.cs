using System.Net;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Egress;

/// <summary>Creates HTTP clients that send through a lease's exit.</summary>
/// <remarks>One handler per lease, keyed by its proxy and sticky key and disposed when the lease is released; a direct
/// lease keeps the safe outbound client's connect-time address check. Generic retries stay off: retrying belongs to the
/// ladder.</remarks>
public interface IEgressClientFactory
{
    /// <summary>Creates a client over the lease's exit, carrying the given cookie jar.</summary>
    /// <param name="lease">The exit to send through.</param>
    /// <param name="cookies">The cookie jar of the session, or none.</param>
    /// <returns>A client the caller disposes; its handler outlives it until the lease is released.</returns>
    HttpClient CreateClient(EgressLease lease, CookieContainer? cookies = null);
}
