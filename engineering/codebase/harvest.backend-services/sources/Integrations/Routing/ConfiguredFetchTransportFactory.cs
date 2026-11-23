using System.Net;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Proxy;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Sessions;
using WoW.Two.Sdk.Beta.Be.Http.Safety;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Routing;

/// <summary>Selects one exact-origin route before fetching and holds it fixed for the whole request.</summary>
/// <remarks>A fixed proxy route (an inline proxy or a fixed profile) reuses one guarded client, and so one connection pool, for the
/// process: the lease of each fetch borrows it and every per-fetch check (the enabled switch, the route match, the profile and its
/// lease, the destination) still runs. A route that leases its IP from a broker builds a client for each lease. Disposing the
/// factory, which a host does at shutdown, disposes the shared clients.</remarks>
public sealed class ConfiguredFetchTransportFactory(
    IHttpClientFactory clients,
    IOptions<HarvestIntegrationOptions> options,
    AccountSessionPool sessions,
    IProxyHttpClientFactory proxies,
    OutboundProxyBroker broker,
    TimeProvider clock) : IFetchTransportFactory, IAsyncDisposable
{
    private readonly SharedProxyClients shared = new();

    /// <inheritdoc />
    public ValueTask DisposeAsync() => shared.DisposeAsync();

    /// <inheritdoc />
    public ValueTask<IFetchTransportLease> AcquireAsync(
        Uri destination, CookieContainer? runCookies = null, CancellationToken cancellationToken = default) =>
        AcquireAsync(destination, runCookies, allowPrefix: false, cancellationToken);

    /// <inheritdoc />
    public async ValueTask<IFetchTransportLease> AcquireAsync(
        Uri destination,
        CookieContainer? runCookies,
        bool allowPrefix,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IntegrationValidation.IsWeb(destination))
            throw new FetchTransportUnavailableException("The source destination is invalid.");
        HarvestIntegrationOptions settings = options.Value;
        if (!settings.Enabled)
            return Direct(destination, runCookies, allowPrefix);
        if (settings.Routes is null || settings.Routes.Any(route => route is null
            || !IntegrationValidation.TryOrigin(route.Origin, out _) || route.SessionIds is null))
            throw new FetchTransportUnavailableException("Integration route origins and session lists must be valid.");
        List<FetchRouteOptions> routes = settings.Routes.Where(route => IntegrationValidation.TryOrigin(route.Origin, out Uri configured)
            && IntegrationValidation.SameOrigin(configured, destination)).ToList();
        ProxyConnectionOptions? inlineOlx = string.IsNullOrWhiteSpace(settings.OlxProxy?.Server) ? null : settings.OlxProxy;
        if ((inlineOlx is not null || !string.IsNullOrWhiteSpace(settings.OlxProxyProfile))
            && destination.Scheme == Uri.UriSchemeHttps && destination.IsDefaultPort
            && destination.IdnHost.TrimEnd('.') is "www.olx.uz" or "olx.uz")
        {
            routes.Add(new FetchRouteOptions
            {
                Key = "olx-ip-server", Mode = FetchRouteMode.Proxy, Proxy = inlineOlx, ProxyProfile = settings.OlxProxyProfile,
            });
        }
        if (routes.Count == 0)
            return Direct(destination, runCookies, allowPrefix);
        if (routes.Count != 1)
            throw new FetchTransportUnavailableException("Exactly one integration route may match a source origin.");
        FetchRouteOptions route = routes[0];
        if (string.IsNullOrWhiteSpace(route.Key) || route.Key.Length > 80 || !Enum.IsDefined(route.Mode)
            || route.Key.Any(character => !char.IsAsciiLetterOrDigit(character) && character is not ('-' or '_' or '.')))
            throw new FetchTransportUnavailableException("The source integration route is invalid.");
        OutboundProxyProfileOptions? profile = null;
        if (!string.IsNullOrWhiteSpace(route.ProxyProfile)
            && (route.Mode != FetchRouteMode.Proxy || route.Proxy is not null || route.IpLease is not null
                || settings.ProxyProfiles is not { } profiles || !profiles.TryGetValue(route.ProxyProfile, out profile)))
            throw new FetchTransportUnavailableException("The source proxy profile is unknown or conflicts with inline settings.");
        if (route.Mode == FetchRouteMode.IpLease)
            profile = new OutboundProxyProfileOptions { Broker = route.IpLease };

        AccountSessionLease? account = null;
        OutboundProxyLease? allocated = null;
        HttpClient? client = null;
        bool sharedClient = false;
        bool stopEarly = allowPrefix && (route.Mode == FetchRouteMode.Direct || proxies.SupportsEarlyBodyStop);
        // Keep the two connection pools distinct. The lease retains the route key for robots and pacing.
        string poolKey = stopEarly ? route.Key + "/prefix" : route.Key;
        try
        {
            if (route.SessionIds.Count > 0)
                account = sessions.Acquire(destination, route.SessionIds);
            if (profile is not null)
            {
                allocated = await broker.AcquireAsync(profile, destination, route.Key, cancellationToken);
                if (profile.Broker is null)
                {
                    OutboundProxyProfileOptions fixedProfile = profile;
                    string settingsKey = SharedProxyClients.Settings("profile", fixedProfile.Server, fixedProfile.Username, fixedProfile.Password);
                    client = await shared.GetAsync(destination, poolKey, settingsKey, async () =>
                    {
                        // The handler is bound to the lease it is built from, so the shared client keeps a lease of its own, which
                        // no fetch releases.
                        OutboundProxyLease owner = await broker.AcquireAsync(fixedProfile, destination, route.Key, cancellationToken);
                        try
                        {
                            return new SharedProxyClients.Entry(settingsKey, proxies.CreateClient(owner, stopEarly), owner);
                        }
                        catch
                        {
                            await owner.DisposeAsync();
                            throw;
                        }
                    }, cancellationToken);
                    sharedClient = true;
                }
                else
                {
                    client = proxies.CreateClient(allocated, stopEarly);
                }
            }
            else if (route.Mode == FetchRouteMode.Direct)
            {
                client = clients.CreateClient(stopEarly ? "Harvest.Direct.Prefix" : "Harvest.Direct");
            }
            else
            {
                ProxyConnectionOptions inline = route.Proxy
                    ?? throw new FetchTransportUnavailableException("The source proxy settings are missing.");
                string settingsKey = SharedProxyClients.Settings("inline", inline.Server, inline.Username, inline.Password);
                client = await shared.GetAsync(destination, poolKey, settingsKey,
                    () => ValueTask.FromResult(new SharedProxyClients.Entry(settingsKey, proxies.CreateClient(destination, inline, stopEarly), null)), cancellationToken);
                sharedClient = true;
            }
            CookieContainer? jar = account?.Cookies ?? runCookies;
            // A fixed exit with no session and no caller jar answers robots the same way to every lease of the route.
            bool robotsByRoute = route.Mode != FetchRouteMode.Direct && account is null && jar is null;
            return new IntegrationFetchLease(client, destination, jar, route.Mode != FetchRouteMode.Direct || account is not null,
                route.Key, account, allocated, clock, ownsClient: !sharedClient, cachesRobotsByRoute: robotsByRoute, supportsEarlyBodyStop: stopEarly);
        }
        catch (Exception error)
        {
            if (!sharedClient)
                client?.Dispose();
            account?.Dispose();
            if (allocated is not null)
                await allocated.DisposeAsync();
            if (error is OutboundProxyUnavailableException)
                throw new FetchTransportUnavailableException(error.Message);
            throw;
        }
    }

    private IFetchTransportLease Direct(Uri source, CookieContainer? runCookies, bool allowPrefix)
    {
        return new IntegrationFetchLease(clients.CreateClient(allowPrefix ? "Harvest.Direct.Prefix" : "Harvest.Direct"), source, runCookies,
            runCookies is not null, "direct", null, null, clock, supportsEarlyBodyStop: allowPrefix);
    }
}
