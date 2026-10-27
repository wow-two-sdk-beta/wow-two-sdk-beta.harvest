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
    internal HarvestFetchOptions Options { get; } = new() { MinimumDelay = TimeSpan.Zero };
    internal IPageFetcher Fetcher => new PageFetcher(new Factory(this), Clock);

    internal static HttpResponseMessage Response(HttpStatusCode status = HttpStatusCode.OK, string body = "content", string type = "text/html") =>
        new(status) { Content = new StringContent(body, Encoding.UTF8, type) };

    private sealed class Factory(FetchTestHost host) : IFetchClientFactory
    {
        public HttpClient CreateClient(CookieContainer? cookies = null) => new(new HarvestFetchHandler(
            new HttpClient(host.Transport, disposeHandler: false), host.Hosts, host.Options, new GenericChallengeDetector(host.Clock), host.Clock, cookies));
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

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Requests.Add((request.RequestUri!, request.Headers.ToDictionary(header => header.Key, header => string.Join(", ", header.Value), StringComparer.OrdinalIgnoreCase)));
            return Task.FromResult(Responses.Dequeue());
        }
    }

    internal sealed class MemoryHosts : IHostFetchStore
    {
        internal HostFetchState State { get; } = new() { AdaptiveDelayMilliseconds = 0, MinimumDelayMilliseconds = 0 };
        private readonly Dictionary<string, ResponseValidators> _validators = [];
        private readonly SemaphoreSlim _gate = new(1, 1);

        public async Task<IHostFetchLease> AcquireAsync(string host, CancellationToken cancellationToken)
        {
            await _gate.WaitAsync(cancellationToken);
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
                host._gate.Release();
                return ValueTask.CompletedTask;
            }
        }
    }
}
