using System.Net;
using System.Text;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;

namespace Harvest.Tests.Unit.Capabilities;

/// <summary>Runs real policy handlers over scripted responses and an in-memory durable-store substitute.</summary>
internal sealed class FetchTestHost
{
    internal readonly TestClock Clock = new();
    internal readonly MemoryHosts Hosts = new();
    internal readonly ScriptedTransport Transport = new();
    internal IFetchTransportFactory? TransportFactory { get; set; }
    internal HarvestFetchOptions Options { get; } = new() { MinimumDelay = TimeSpan.Zero };
    internal IPageFetcher Fetcher => new PageFetcher(new Factory(this), Clock);

    internal static HttpResponseMessage Response(HttpStatusCode status = HttpStatusCode.OK, string body = "content", string type = "text/html") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, type) };

    private sealed class Factory(FetchTestHost host) : IFetchClientFactory
    {
        public HttpClient CreateClient(CookieContainer? cookies = null) => new(new HarvestFetchHandler(
            host.TransportFactory ?? new TestTransportFactory(host.Transport), host.Hosts, host.Options, new GenericChallengeDetector(host.Clock), host.Clock, cookies));
    }

    private sealed class TestTransportFactory(HttpMessageHandler handler) : IFetchTransportFactory
    {
        public ValueTask<IFetchTransportLease> AcquireAsync(Uri destination, CookieContainer? runCookies = null, CancellationToken cancellationToken = default)
        {
            return ValueTask.FromResult<IFetchTransportLease>(new Lease(new HttpClient(handler, disposeHandler: false), destination, runCookies));
        }

        private sealed class Lease(HttpClient client, Uri origin, CookieContainer? cookies) : IFetchTransportLease
        {
            public HttpClient Client => client;
            public CookieContainer? Cookies => cookies;
            public bool IsPrivate => Cookies is not null;
            public string RouteKey => "test";
            public bool Allows(Uri destination) => destination.IdnHost == origin.IdnHost;
            public ValueTask DisposeAsync()
            {
                Client.Dispose();
                return ValueTask.CompletedTask;
            }
        }
    }

    internal sealed class TestClock : TimeProvider
    {
        internal DateTimeOffset Now { get; set; } = new(2026, 10, 27, 10, 0, 0, TimeSpan.Zero);
        public override DateTimeOffset GetUtcNow() => Now;
    }

    internal sealed class ScriptedTransport : HttpMessageHandler
    {
        internal Queue<HttpResponseMessage> Responses { get; } = [];
        internal List<(Uri Uri, Dictionary<string, string> Headers)> Requests { get; } = [];
        internal Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>>? SendOverride { get; set; }

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!, request.Headers.ToDictionary(header => header.Key, header => string.Join(", ", header.Value), StringComparer.OrdinalIgnoreCase)));
            return SendOverride?.Invoke(request, cancellationToken) ?? Task.FromResult(Responses.Dequeue());
        }
    }

    internal sealed class MemoryHosts : IHostFetchStore
    {
        internal HostFetchState State { get; } = new() { AdaptiveDelayMilliseconds = 0, MinimumDelayMilliseconds = 0 };
        internal int Acquisitions { get; private set; }
        internal int Disposals { get; private set; }
        private readonly Dictionary<string, ResponseValidators> _validators = [];
        private readonly SemaphoreSlim _gate = new(1, 1);

        public async Task<IHostFetchLease> AcquireAsync(string host, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
            Acquisitions++;
            return new Lease(this);
        }

        private sealed class Lease(MemoryHosts host) : IHostFetchLease
        {
            public HostFetchState State => host.State;
            public Task SaveAsync(CancellationToken cancellationToken) => Task.CompletedTask;
            public Task<ResponseValidators?> GetValidatorsAsync(string url, CancellationToken cancellationToken) =>
                Task.FromResult(host._validators.GetValueOrDefault(url));
            public Task SaveValidatorsAsync(string url, ResponseValidators validators, CancellationToken cancellationToken)
            {
                host._validators[url] = validators;
                return Task.CompletedTask;
            }
            public ValueTask DisposeAsync()
            {
                host.Disposals++;
                host._gate.Release();
                return ValueTask.CompletedTask;
            }
        }
    }
}
