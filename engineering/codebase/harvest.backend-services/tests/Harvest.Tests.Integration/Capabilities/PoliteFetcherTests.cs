using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace Harvest.Tests.Integration.Capabilities;

/// <summary>The polite fetcher over a real socket: per-host pacing and robots, conditional requests and 304,
/// <c>Retry-After</c>, and telling a challenge from a block. A refusal is recorded and backs the host off — never worked
/// around.</summary>
public sealed class PoliteFetcherTests
{
    private const string Agent = "HarvestBot/1.0 (+https://example.test)";
    private static readonly DateTimeOffset Now = new(2026, 10, 27, 9, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Fetch_ShouldReturnTheDocumentAndPaceByTheCrawlDelay_WhenRobotsAllows()
    {
        await using var fixture = FixtureHost.Start(request => request.Path == "/robots.txt"
            ? FixtureReply.Text("User-agent: *\nDisallow: /private\nCrawl-delay: 5\n")
            : FixtureReply.Html("<html><body>events this week</body></html>", 200, ("ETag", "\"v1\"")));
        await using var host = await StartAsync();
        var hostKey = fixture.BaseUri.Authority;

        var result = await FetchAsync(host, new FetchRequest { Url = new Uri(fixture.BaseUri, "/ru/events"), HostKey = hostKey });

        result.Outcome.Should().Be(FetchOutcome.Ok);
        result.Document.Should().NotBeNull();
        Encoding.UTF8.GetString(result.Document!.Body.Span).Should().Contain("events this week");
        result.ETag.Should().Be("\"v1\"");
        fixture.Received.Should().ContainSingle(request => request.Path == "/ru/events")
            .Which.Header("User-Agent").Should().Be(Agent);
        (await DelaySeconds(host, hostKey)).Should().BeApproximately(5, 0.05);
        (await host.ScalarAsync<int>($"SELECT crawl_delay_s FROM harvest_host_state WHERE host_key = '{hostKey}'")).Should().Be(5);
    }

    [Fact]
    public async Task Fetch_ShouldRecordRobotsDisallowedAndNotFetchThePath_WhenRobotsForbidsIt()
    {
        await using var fixture = FixtureHost.Start(request => request.Path == "/robots.txt"
            ? FixtureReply.Text("User-agent: *\nDisallow: /private\n")
            : FixtureReply.Html("<html>secret</html>"));
        await using var host = await StartAsync();

        var result = await FetchAsync(host, new FetchRequest { Url = new Uri(fixture.BaseUri, "/private/report"), HostKey = fixture.BaseUri.Authority });

        result.Outcome.Should().Be(FetchOutcome.RobotsDisallowed);
        result.Document.Should().BeNull();
        fixture.Received.Should().OnlyContain(request => request.Path == "/robots.txt");
    }

    [Fact]
    public async Task Fetch_ShouldSendTheConditionalHeadersAndReturnNotModified_WhenTheHostAnswers304()
    {
        await using var fixture = FixtureHost.Start(request =>
        {
            if (request.Path == "/robots.txt")
                return FixtureReply.Text("User-agent: *\nDisallow:\n");
            return request.Header("If-None-Match") == "\"v1\""
                ? FixtureReply.Status_(304)
                : FixtureReply.Html("<html>fresh</html>");
        });
        await using var host = await StartAsync();

        var result = await FetchAsync(host, new FetchRequest
        {
            Url = new Uri(fixture.BaseUri, "/ru/events"),
            HostKey = fixture.BaseUri.Authority,
            ETag = "\"v1\"",
            LastModifiedUtc = Now.AddDays(-1),
        });

        result.Outcome.Should().Be(FetchOutcome.NotModified);
        result.StatusCode.Should().Be(304);
        result.Document.Should().BeNull();
        var page = fixture.Received.Single(request => request.Path == "/ru/events");
        page.Header("If-None-Match").Should().Be("\"v1\"");
        page.Header("If-Modified-Since").Should().NotBeNull();
    }

    [Fact]
    public async Task Fetch_ShouldBackOffForTheRetryAfterSeconds_WhenRateLimited()
    {
        await using var fixture = FixtureHost.Start(request => request.Path == "/robots.txt"
            ? FixtureReply.Text("User-agent: *\nDisallow:\n")
            : FixtureReply.Status_(429, ("Retry-After", "120")));
        await using var host = await StartAsync();
        var hostKey = fixture.BaseUri.Authority;

        var result = await FetchAsync(host, new FetchRequest { Url = new Uri(fixture.BaseUri, "/ru/events"), HostKey = hostKey });

        result.Outcome.Should().Be(FetchOutcome.RateLimited);
        result.RetryAfter.Should().Be(TimeSpan.FromSeconds(120));
        (await DelaySeconds(host, hostKey)).Should().BeApproximately(120, 0.05);
        (await host.ScalarAsync<bool>($"SELECT retry_after_until IS NOT NULL FROM harvest_host_state WHERE host_key = '{hostKey}'")).Should().BeTrue();
        (await host.ScalarAsync<string>($"SELECT pause_reason FROM harvest_host_state WHERE host_key = '{hostKey}'")).Should().Be("rate_limit");
        (await host.ScalarAsync<int>($"SELECT consecutive_failures FROM harvest_host_state WHERE host_key = '{hostKey}'")).Should().Be(1);
    }

    [Fact]
    public async Task Fetch_ShouldBackOffUntilTheRetryAfterDate_WhenTheHostGivesAnHttpDate()
    {
        var header = Now.AddSeconds(90).ToString("R");
        await using var fixture = FixtureHost.Start(request => request.Path == "/robots.txt"
            ? FixtureReply.Text("User-agent: *\nDisallow:\n")
            : FixtureReply.Status_(503, ("Retry-After", header)));
        await using var host = await StartAsync();
        var hostKey = fixture.BaseUri.Authority;

        var result = await FetchAsync(host, new FetchRequest { Url = new Uri(fixture.BaseUri, "/ru/events"), HostKey = hostKey });

        result.Outcome.Should().Be(FetchOutcome.RateLimited);
        (await DelaySeconds(host, hostKey)).Should().BeApproximately(90, 1);
    }

    [Fact]
    public async Task Fetch_ShouldNameAChallengeAndPause_WhenAnInterstitialStandsInTheWay()
    {
        await using var fixture = FixtureHost.Start(request => request.Path == "/robots.txt"
            ? FixtureReply.Text("User-agent: *\nDisallow:\n")
            : FixtureReply.Html("<html><head><title>Just a moment...</title></head><body></body></html>", 403));
        await using var host = await StartAsync();
        var hostKey = fixture.BaseUri.Authority;

        var result = await FetchAsync(host, new FetchRequest { Url = new Uri(fixture.BaseUri, "/ru/events"), HostKey = hostKey });

        result.Outcome.Should().Be(FetchOutcome.Challenge);
        result.Challenge!.Kind.Should().Be(ChallengeKind.JsInterstitial);
        (await host.ScalarAsync<string>($"SELECT pause_reason FROM harvest_host_state WHERE host_key = '{hostKey}'")).Should().Be("challenge");
        (await host.ScalarAsync<string>($"SELECT last_challenge_kind FROM harvest_host_lane WHERE host_key = '{hostKey}'")).Should().Be("js_interstitial");
        (await host.ScalarAsync<bool>($"SELECT paused_until IS NOT NULL FROM harvest_host_state WHERE host_key = '{hostKey}'")).Should().BeTrue();
    }

    [Fact]
    public async Task Fetch_ShouldNameABlock_WhenTheHostFlatlyRefuses()
    {
        await using var fixture = FixtureHost.Start(request => request.Path == "/robots.txt"
            ? FixtureReply.Text("User-agent: *\nDisallow:\n")
            : FixtureReply.Html("<html><body>Forbidden</body></html>", 403));
        await using var host = await StartAsync();
        var hostKey = fixture.BaseUri.Authority;

        var result = await FetchAsync(host, new FetchRequest { Url = new Uri(fixture.BaseUri, "/ru/events"), HostKey = hostKey });

        result.Outcome.Should().Be(FetchOutcome.Blocked);
        (await host.ScalarAsync<string>($"SELECT pause_reason FROM harvest_host_state WHERE host_key = '{hostKey}'")).Should().Be("blocked");
        (await host.ScalarAsync<int>($"SELECT consecutive_failures FROM harvest_host_state WHERE host_key = '{hostKey}'")).Should().Be(1);
    }

    [Fact]
    public async Task Reserve_ShouldReportTheHostNotDue_AfterItHasBeenRateLimited()
    {
        await using var fixture = FixtureHost.Start(request => request.Path == "/robots.txt"
            ? FixtureReply.Text("User-agent: *\nDisallow:\n")
            : FixtureReply.Status_(429, ("Retry-After", "120")));
        await using var host = await StartAsync();
        var hostKey = fixture.BaseUri.Authority;
        using var scope = host.Services.CreateScope();
        var fetcher = scope.ServiceProvider.GetRequiredService<IPoliteFetcher>();

        await fetcher.FetchAsync(new FetchRequest { Url = new Uri(fixture.BaseUri, "/ru/events"), HostKey = hostKey });
        var gate = await fetcher.ReserveAsync(hostKey);

        gate.Allowed.Should().BeFalse();
        gate.Wait.Should().BeGreaterThan(TimeSpan.FromSeconds(60));
    }

    private static Task<ModuleHost> StartAsync() => ModuleHost.StartAsync(configure: options =>
    {
        options.Fetching.AllowPrivateNetworkTargets = true;
        options.Fetching.UserAgent = Agent;
    });

    private static async Task<FetchResult> FetchAsync(ModuleHost host, FetchRequest request)
    {
        using var scope = host.Services.CreateScope();
        return await scope.ServiceProvider.GetRequiredService<IPoliteFetcher>().FetchAsync(request);
    }

    private static async Task<double> DelaySeconds(ModuleHost host, string hostKey) =>
        await host.ScalarAsync<double>(
            $"SELECT EXTRACT(EPOCH FROM (next_allowed_at - timestamptz '2026-10-27 09:00:00+00'))::float8 FROM harvest_host_state WHERE host_key = '{hostKey}'");
}
