using System.Net;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Proxy;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Sessions;
using WoW.Two.Sdk.Beta.Be.Http.Safety;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Routing;

/// <summary>Selects one exact-origin route before fetching and holds it fixed for the whole request.</summary>
public sealed class ConfiguredFetchTransportFactory(
    IHttpClientFactory clients,
    IOptions<HarvestIntegrationOptions> options,
    AccountSessionPool sessions,
    IProxyHttpClientFactory proxies,
    OutboundProxyBroker broker,
    TimeProvider clock) : IFetchTransportFactory
{
    /// <inheritdoc />
    public async ValueTask<IFetchTransportLease> AcquireAsync(
        Uri destination,
        CookieContainer? runCookies = null,
        CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!IntegrationValidation.IsWeb(destination))
            throw new FetchTransportUnavailableException("The source destination is invalid.");
        HarvestIntegrationOptions settings = options.Value;
        if (!settings.Enabled)
            return Direct(destination, runCookies);
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
            return Direct(destination, runCookies);
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
        try
        {
            if (route.SessionIds.Count > 0)
                account = sessions.Acquire(destination, route.SessionIds);
            if (profile is not null)
            {
                allocated = await broker.AcquireAsync(profile, destination, route.Key, cancellationToken);
                client = proxies.CreateClient(allocated);
            }
            else if (route.Mode == FetchRouteMode.Direct)
            {
                client = clients.CreateClient("Harvest.Direct");
            }
            else
            {
                client = proxies.CreateClient(destination, route.Proxy
                    ?? throw new FetchTransportUnavailableException("The source proxy settings are missing."));
            }
            return new IntegrationFetchLease(client, destination, account?.Cookies ?? runCookies,
                route.Mode != FetchRouteMode.Direct || account is not null, route.Key, account, allocated, clock);
        }
        catch (Exception error)
        {
            client?.Dispose();
            account?.Dispose();
            if (allocated is not null)
                await allocated.DisposeAsync();
            if (error is OutboundProxyUnavailableException)
                throw new FetchTransportUnavailableException(error.Message);
            throw;
        }
    }

    private IFetchTransportLease Direct(Uri source, CookieContainer? runCookies)
    {
        return new IntegrationFetchLease(clients.CreateClient("Harvest.Direct"), source, runCookies,
            runCookies is not null, "direct", null, null, clock);
    }
}
