using System.Net;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Sessions;
using WoW.Two.Sdk.Beta.Be.Http.Safety;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Routing;

/// <summary>Fences one fetch to its selected origin, account and finite IP allocation.</summary>
/// <remarks>A client the lease does not own is a shared proxy client (<c>SharedProxyClients</c>): disposing the lease leaves it, and
/// its connections, for the next fetch.</remarks>
internal sealed class IntegrationFetchLease(
    HttpClient client,
    Uri origin,
    CookieContainer? cookies,
    bool isPrivate,
    string routeKey,
    AccountSessionLease? account,
    OutboundProxyLease? proxy,
    TimeProvider clock,
    bool ownsClient = true,
    bool cachesRobotsByRoute = false) : IFetchTransportLease
{
    private int disposed;
    public HttpClient Client { get; } = client;
    public CookieContainer? Cookies { get; } = cookies;
    public bool IsPrivate { get; } = isPrivate;
    public string RouteKey { get; } = routeKey;
    public bool CachesRobotsByRoute { get; } = cachesRobotsByRoute;

    public bool Allows(Uri destination)
    {
        return Volatile.Read(ref disposed) == 0 && IntegrationValidation.SameOrigin(origin, destination)
            && (account is null || account.ExpiresAtUtc > clock.GetUtcNow())
            && (proxy is null || proxy.IsActive);
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref disposed, 1) != 0)
            return;
        try
        {
            if (ownsClient)
                Client.Dispose();
        }
        finally
        {
            try
            {
                account?.Dispose();
            }
            finally
            {
                if (proxy is not null)
                    await proxy.DisposeAsync();
            }
        }
    }
}
