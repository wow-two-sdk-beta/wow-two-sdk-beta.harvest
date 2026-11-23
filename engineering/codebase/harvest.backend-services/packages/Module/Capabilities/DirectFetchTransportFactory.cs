using System.Net;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;

/// <summary>Provides the default guarded route from the host's own public connection.</summary>
internal sealed class DirectFetchTransportFactory(IHttpClientFactory clients) : IFetchTransportFactory
{
    public ValueTask<IFetchTransportLease> AcquireAsync(
        Uri destination, CookieContainer? runCookies = null, CancellationToken cancellationToken = default) =>
        AcquireAsync(destination, runCookies, allowPrefix: false, cancellationToken);

    public ValueTask<IFetchTransportLease> AcquireAsync(
        Uri destination,
        CookieContainer? runCookies,
        bool allowPrefix,
        CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        string transport = allowPrefix ? HarvestFetchClientFactory.PrefixTransportName : HarvestFetchClientFactory.TransportName;
        IFetchTransportLease lease = new DirectLease(clients.CreateClient(transport), destination, runCookies, allowPrefix);
        return ValueTask.FromResult(lease);
    }

    private sealed class DirectLease(HttpClient client, Uri origin, CookieContainer? cookies, bool allowPrefix) : IFetchTransportLease
    {
        public HttpClient Client { get; } = client;
        public CookieContainer? Cookies { get; } = cookies;
        public bool IsPrivate => Cookies is not null;
        public string RouteKey => "direct";
        public bool SupportsEarlyBodyStop => allowPrefix;

        public bool Allows(Uri destination)
        {
            return destination.Scheme is "http" or "https" && destination.UserInfo.Length == 0
                && destination.IdnHost.Equals(origin.IdnHost, StringComparison.OrdinalIgnoreCase)
                && (destination.GetLeftPart(UriPartial.Authority).Equals(origin.GetLeftPart(UriPartial.Authority), StringComparison.OrdinalIgnoreCase)
                    || origin.Scheme == "http" && origin.IsDefaultPort && destination.Scheme == "https" && destination.IsDefaultPort);
        }

        public ValueTask DisposeAsync()
        {
            Client.Dispose();
            return ValueTask.CompletedTask;
        }
    }
}
