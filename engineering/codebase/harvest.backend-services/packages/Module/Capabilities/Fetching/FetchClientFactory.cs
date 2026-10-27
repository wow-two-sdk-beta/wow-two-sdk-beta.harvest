using System.Net;
using System.Net.Sockets;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Http.Safety;
using WoW.Two.Sdk.Beta.Be.Http.Safety.Validators;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities.Fetching;

/// <summary>Creates the direct HTTP clients Harvest fetches with: one honest user agent, a run's own cookie jar, one
/// connection per host, and a connect that only reaches public addresses — core's safe-outbound policy.</summary>
/// <remarks>There is no other route. Redirects, proxies and generic retries are off: a challenge, a block or a rate limit
/// is an outcome the caller records and backs off, never something to retry past.</remarks>
internal sealed class FetchClientFactory(IOptions<HarvestModuleOptions> options) : IFetchClientFactory
{
    private readonly OutboundAddressValidator validator = new();
    private readonly FetchingOptions settings = options.Value.Fetching;

    /// <inheritdoc />
    public HttpClient CreateClient(CookieContainer? cookies = null)
    {
        var handler = new SocketsHttpHandler
        {
            AllowAutoRedirect = false,
            UseProxy = false,
            Proxy = null,
            AutomaticDecompression = DecompressionMethods.All,
            MaxConnectionsPerServer = 1,
            PooledConnectionLifetime = TimeSpan.FromMinutes(1),
            ConnectTimeout = settings.ConnectTimeout,
            CookieContainer = cookies ?? new CookieContainer(),
            UseCookies = true,
            ConnectCallback = ConnectAsync,
        };

        var client = new HttpClient(handler, disposeHandler: true) { Timeout = settings.Timeout };
        client.DefaultRequestHeaders.UserAgent.Clear();
        client.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", settings.UserAgent);
        return client;
    }

    /// <summary>Resolves the host and connects only to an address the outbound policy permits, so a name that resolves to
    /// a private or loopback address cannot steer a fetch at an internal service.</summary>
    private async ValueTask<Stream> ConnectAsync(SocketsHttpConnectionContext context, CancellationToken cancellationToken)
    {
        var host = context.DnsEndPoint.Host;
        var port = context.DnsEndPoint.Port;
        var addresses = IPAddress.TryParse(host, out var literal)
            ? [literal]
            : await Dns.GetHostAddressesAsync(host, cancellationToken);

        foreach (var address in addresses)
        {
            if (!settings.AllowPrivateNetworkTargets && !validator.IsAllowed(address))
                continue;

            var socket = new Socket(address.AddressFamily, SocketType.Stream, ProtocolType.Tcp) { NoDelay = true };
            try
            {
                await socket.ConnectAsync(new IPEndPoint(address, port), cancellationToken);
                return new NetworkStream(socket, ownsSocket: true);
            }
            catch
            {
                socket.Dispose();
            }
        }

        throw new OutboundAddressBlockedException($"No permitted address answered for host '{host}'.");
    }
}
