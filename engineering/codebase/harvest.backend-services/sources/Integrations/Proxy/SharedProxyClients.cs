using System.Security.Cryptography;
using System.Text;
using WoW.Two.Sdk.Beta.Be.Http.Safety;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Proxy;

/// <summary>Keeps one guarded proxy client, and so one connection pool, for each fixed proxy route and source origin for the
/// life of the process.</summary>
/// <remarks>A client is keyed by its route and origin, and valid only for the settings it was built from (<see cref="Settings"/>):
/// a route whose proxy server or credentials changed builds a new client, and the old one stays until the process ends, so a fetch
/// still reading through it is not cut off. A client is shared by concurrent fetches; the per-request guards (the destination check,
/// the route match, the lease check and the integration switch) stay with each fetch, not with the client. A lease from an IP-lease
/// broker is never shared. Disposal at host shutdown disposes every client and the lease behind it.</remarks>
internal sealed class SharedProxyClients : IAsyncDisposable
{
    private readonly object gate = new();
    private readonly SemaphoreSlim building = new(1, 1);
    private readonly Dictionary<(string Origin, string Route), Entry> current = [];
    private readonly List<Entry> retired = [];
    private bool disposed;

    /// <summary>Gets the shared client of a route and origin, building it with <paramref name="create"/> on first use or when the
    /// route's settings changed; builds are one at a time, so concurrent first fetches share one client.</summary>
    /// <param name="destination">The source URL; its origin keys the client.</param>
    /// <param name="routeKey">The route's key.</param>
    /// <param name="settings">The route's effective proxy settings, from <see cref="Settings"/>.</param>
    /// <param name="create">Builds the client; a failure caches nothing.</param>
    /// <param name="cancellationToken">Cancels the wait for another build.</param>
    internal async ValueTask<HttpClient> GetAsync(Uri destination, string routeKey, string settings, Func<ValueTask<Entry>> create,
        CancellationToken cancellationToken = default)
    {
        var slot = (destination.GetLeftPart(UriPartial.Authority), routeKey);
        if (Find(slot, settings) is { } hit)
            return hit;

        await building.WaitAsync(cancellationToken);
        try
        {
            if (Find(slot, settings) is { } raced)
                return raced;
            Entry built = await create();
            lock (gate)
            {
                if (!disposed)
                {
                    if (current.Remove(slot, out Entry? replaced))
                        retired.Add(replaced);
                    current[slot] = built;
                    return built.Client;
                }
            }

            await built.DisposeAsync();
            throw new ObjectDisposedException(nameof(SharedProxyClients));
        }
        finally
        {
            building.Release();
        }
    }

    private HttpClient? Find((string Origin, string Route) slot, string settings)
    {
        lock (gate)
        {
            ObjectDisposedException.ThrowIf(disposed, this);
            return current.TryGetValue(slot, out Entry? entry) && entry.Settings == settings ? entry.Client : null;
        }
    }

    /// <summary>Gets what identifies a route's effective proxy settings: the kind, server, username and a hash of the password.</summary>
    internal static string Settings(string kind, string? server, string? username, string? password) => string.Join('\n', kind,
        server, username, password is null ? string.Empty : Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(password))));

    public async ValueTask DisposeAsync()
    {
        Entry[] all;
        lock (gate)
        {
            if (disposed)
                return;
            disposed = true;
            all = [.. current.Values, .. retired];
            current.Clear();
            retired.Clear();
        }

        List<Exception> failures = [];
        foreach (Entry entry in all)
        {
            try
            {
                await entry.DisposeAsync();
            }
            catch (Exception error)
            {
                failures.Add(error);
            }
        }

        if (failures.Count > 0)
            throw new AggregateException(failures);
    }

    /// <summary>A shared client with the settings it was built from and the proxy lease its handler is bound to, if any.</summary>
    internal sealed class Entry(string settings, HttpClient client, OutboundProxyLease? owner) : IAsyncDisposable
    {
        internal string Settings { get; } = settings;
        internal HttpClient Client { get; } = client;

        public async ValueTask DisposeAsync()
        {
            try
            {
                Client.Dispose();
            }
            finally
            {
                if (owner is not null)
                    await owner.DisposeAsync();
            }
        }
    }
}
