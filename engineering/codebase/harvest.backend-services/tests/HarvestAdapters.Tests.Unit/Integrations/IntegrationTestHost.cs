using System.Net;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Captcha;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Proxy;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Routing;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Sessions;
using WoW.Two.Sdk.Beta.Be.Http.Safety;

namespace HarvestAdapters.Tests.Unit.Integrations;

internal sealed class IntegrationTestHost
{
    internal FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));
    internal HarvestIntegrationOptions Options { get; } = new();
    internal StubHttpFactory Http { get; } = new();
    internal StubProxyFactory Proxies { get; } = new();
    internal AccountSessionPool Sessions { get; }
    internal ConfiguredFetchTransportFactory Routes { get; }
    internal TwoCaptchaSolver Captcha { get; }

    internal IntegrationTestHost()
    {
        Sessions = new(Microsoft.Extensions.Options.Options.Create(Options), Clock);
        Routes = new(Http, Microsoft.Extensions.Options.Options.Create(Options), Sessions, Proxies, new OutboundProxyBroker(Http, Clock), Clock);
        Captcha = new(Http, Microsoft.Extensions.Options.Options.Create(Options), Clock);
    }

    internal void EnableCaptcha()
    {
        Options.Enabled = true;
        Options.Captcha.Enabled = true;
        Options.Captcha.ApiKey = "provider-test-key";
        Options.Captcha.AllowedOrigins.Add("https://source.example.test");
    }

    internal static CaptchaTask Task()
    {
        return new CaptchaTask { Kind = CaptchaKind.RecaptchaV2, SiteKey = "public-site-key", WebsiteUrl = new Uri("https://source.example.test/event") };
    }

    internal FetchRouteOptions AddRoute(FetchRouteMode mode = FetchRouteMode.Direct)
    {
        Options.Enabled = true;
        var route = new FetchRouteOptions { Key = "source", Origin = "https://source.example.test", Mode = mode };
        Options.Routes.Add(route);
        return route;
    }

    internal void AddSession(string id, string value, TimeSpan? lifetime = null)
    {
        Options.Sessions.Add(new AccountSessionOptions
        {
            Id = id, Origin = "https://source.example.test", ExpiresAtUtc = Clock.GetUtcNow() + (lifetime ?? TimeSpan.FromHours(1)),
            Cookies = [new SessionCookieOptions { Name = "session", Value = value }],
        });
    }
}
internal sealed class StubHttpFactory : IHttpClientFactory
{
    internal List<ObservedRequest> Calls { get; } = [];
    internal List<string> Clients { get; } = [];
    internal Queue<string> Replies { get; } = [];
    internal HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
    internal Func<CancellationToken, Task>? BeforeReply { get; set; }

    public HttpClient CreateClient(string name)
    {
        Clients.Add(name);
        return new HttpClient(new CallbackHandler(async (request, ct) =>
        {
            string? body = request.Content is null ? null : await request.Content.ReadAsStringAsync(ct);
            Calls.Add(new ObservedRequest(request.RequestUri!, request.Method, request.Headers.ToDictionary(
                pair => pair.Key, pair => string.Join(",", pair.Value), StringComparer.OrdinalIgnoreCase), body));
            if (BeforeReply is not null)
                await BeforeReply(ct);
            return new HttpResponseMessage(Status) { Content = new StringContent(Replies.Count > 0 ? Replies.Dequeue() : "{}") };
        }));
    }
}

internal sealed record ObservedRequest(Uri Uri, HttpMethod Method, IReadOnlyDictionary<string, string> Headers, string? Body);

internal sealed class CallbackHandler(Func<HttpRequestMessage, CancellationToken, Task<HttpResponseMessage>> send) : HttpMessageHandler
{
    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        return send(request, cancellationToken);
    }
}

internal sealed class StubProxyFactory : IProxyHttpClientFactory
{
    internal List<Uri> Calls { get; } = [];
    internal List<IReadOnlyDictionary<string, string>> Headers { get; } = [];
    internal int Requests { get; private set; }

    public HttpClient CreateClient(Uri sourceOrigin, ProxyConnectionOptions proxy) => Create(new Uri(proxy.Server));

    public HttpClient CreateClient(OutboundProxyLease lease) => Create(lease.ProxyUri);

    private HttpClient Create(Uri proxy)
    {
        Calls.Add(proxy);
        return new HttpClient(new CallbackHandler((request, _) =>
        {
            Requests++;
            Headers.Add(request.Headers.ToDictionary(pair => pair.Key, pair => string.Join(",", pair.Value), StringComparer.OrdinalIgnoreCase));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.Forbidden));
        }));
    }
}
