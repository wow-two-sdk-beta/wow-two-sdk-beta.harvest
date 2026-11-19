using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;

namespace HarvestAdapters.Tests.Integration.Afisha;

/// <summary>Replays a section listing that links its sibling sections through the Ocharo Afisha pipeline.</summary>
[Collection(SourceDeliveryCollection.Name)]
public sealed class AfishaSectionCrawlTests
{
    private const string Listing = "https://www.afisha.uz/ru/fashion";

    [Fact]
    public async Task FashionRun_ShouldReadOnlyItsOwnSection_AndStopAtFivePages()
    {
        var fetcher = new ReplayFetcher(await File.ReadAllTextAsync(
            Path.Combine(AppContext.BaseDirectory, "Afisha", "Fixtures", "ru-fashion-listing.html")));
        string connectionString = await SourcePostgres.NewDatabaseAsync();
        var services = new ServiceCollection().AddLogging();
        services.AddHarvest().Configure(options => options.ConnectionString = connectionString);
        services.AddHarvestFetching();
        services.AddHarvestReaders().AddHarvestExtraction().AddHarvestPosts().AddHarvestDelivery();
        services.AddHarvestEngine();
        services.AddOcharoEventSources();
        services.AddSingleton<IPageFetcher>(fetcher);
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await provider.MigrateHarvestOnStartupAsync("afisha-section-replay");
        await using var scope = provider.CreateAsyncScope();
        OcharoSetup setup = Value(await scope.ServiceProvider.GetRequiredService<OcharoSourceProvisioner>().EnsureAsync());
        OcharoSourceRegistration route = setup.Sources.First(item => item.SourceKey == "afisha-uz");
        route.Seed.Should().Be(Listing);
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        Value(await registry.GetPipelineAsync(route.PipelineId)).Limits.MaxPages.Should().Be(5);
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        var engine = scope.ServiceProvider.GetRequiredService<HarvestEngine>();

        RunModel queued = Value(await runs.StartAsync(route.PipelineId));
        int iterations = 0;
        while (await engine.ProcessNextAsync())
            if (++iterations > 20) throw new InvalidOperationException("The replayed frontier did not drain.");

        RunModel run = Value(await runs.GetAsync(queued.Id));
        run.Pages.Should().Be(5);
        run.Status.Should().Be(RunStatus.Partial);
        run.CompletionReason.Should().Be(RunReason.PageLimit);
        fetcher.Calls.Should().HaveCount(5);
        fetcher.Calls[0].Should().Be(Listing);
        fetcher.Calls.Skip(1).Should().BeEquivalentTo(
            "https://www.afisha.uz/ru/fashion/2026/11/14/runway-week-closing",
            "https://www.afisha.uz/ru/fashion/2026/11/13/designer-market",
            "https://www.afisha.uz/ru/fashion/2026/11/12/textile-open-call",
            "https://www.afisha.uz/ru/fashion/2026/11/11/student-collections");
        var posts = Value(await scope.ServiceProvider.GetRequiredService<IPostBroker>().ListPostsAsync(setup.ContainerId));
        posts.Should().HaveCount(4).And.OnlyContain(post => post.Base.Title != null
            && post.Base.Title.StartsWith("Fixture ", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SecondRun_ShouldAdmitTheUnseenItems_WhenTheFirstFourAreAlreadyCaptured()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 17, 7, 0, 0, TimeSpan.Zero));
        var fetcher = new ReplayFetcher(await ReadListingAsync(), clock);
        await using ServiceProvider provider = await BuildAsync(fetcher, clock);
        await using var scope = provider.CreateAsyncScope();
        var (setup, route) = await ProvisionAsync(scope);

        string[] first = await CaptureAsync(scope, route, fetcher);
        clock.Advance(TimeSpan.FromHours(6));
        string[] second = await CaptureAsync(scope, route, fetcher);

        ShouldReadTheListingAndOnly(first, "11/14/runway-week-closing", "11/13/designer-market", "11/12/textile-open-call",
            "11/11/student-collections");
        ShouldReadTheListingAndOnly(second, "11/10/atlas-workshop", "11/08/vintage-fair", "11/06/accessories-talk", "11/04/ikat-showcase");
        var posts = Value(await scope.ServiceProvider.GetRequiredService<IPostBroker>().ListPostsAsync(setup.ContainerId));
        posts.Should().HaveCount(8, "the second run reached the items the first run's four slots never did");
    }

    [Fact]
    public async Task EveryItemKnown_ShouldStillRecheckTheOnesNotReadWithinTheCaptureInterval()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 17, 7, 0, 0, TimeSpan.Zero));
        var fetcher = new ReplayFetcher(await ReadListingAsync(), clock);
        await using ServiceProvider provider = await BuildAsync(fetcher, clock);
        await using var scope = provider.CreateAsyncScope();
        var (_, route) = await ProvisionAsync(scope);
        await CaptureAsync(scope, route, fetcher);
        clock.Advance(TimeSpan.FromHours(7));
        await CaptureAsync(scope, route, fetcher);

        // Items 1-4 were read 10 h ago and items 5-8 3 h ago, against the pipeline's 6-hour capture schedule.
        clock.Advance(TimeSpan.FromHours(3));
        string[] third = await CaptureAsync(scope, route, fetcher);
        // Items 1-4 were read 4 h ago and items 5-8 7 h ago.
        clock.Advance(TimeSpan.FromHours(4));
        string[] fourth = await CaptureAsync(scope, route, fetcher);
        // Items 1-4 were read 5 h ago and items 5-8 1 h ago: none is stale, so the page's own order decides.
        clock.Advance(TimeSpan.FromHours(1));
        string[] fifth = await CaptureAsync(scope, route, fetcher);

        ShouldReadTheListingAndOnly(third, "11/14/runway-week-closing", "11/13/designer-market", "11/12/textile-open-call",
            "11/11/student-collections");
        ShouldReadTheListingAndOnly(fourth, "11/10/atlas-workshop", "11/08/vintage-fair", "11/06/accessories-talk", "11/04/ikat-showcase");
        ShouldReadTheListingAndOnly(fifth, "11/14/runway-week-closing", "11/13/designer-market", "11/12/textile-open-call",
            "11/11/student-collections");
    }

    [Fact]
    public async Task StaleItems_ShouldTakeTurnsOldestReadFirst_WhenEveryItemIsStaleAtEachRun()
    {
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 11, 17, 7, 0, 0, TimeSpan.Zero));
        var fetcher = new ReplayFetcher(await ReadListingAsync(), clock);
        await using ServiceProvider provider = await BuildAsync(fetcher, clock);
        await using var scope = provider.CreateAsyncScope();
        var (_, route) = await ProvisionAsync(scope);
        await CaptureAsync(scope, route, fetcher);
        clock.Advance(TimeSpan.FromHours(7));
        await CaptureAsync(scope, route, fetcher);

        // Runs 7 h apart, against a 6-hour capture schedule: every known item is stale at each run, so only the read
        // times differ. Page order alone would take items 1-4 every time and never reach the rest.
        clock.Advance(TimeSpan.FromHours(7));
        string[] third = await CaptureAsync(scope, route, fetcher);
        clock.Advance(TimeSpan.FromHours(7));
        string[] fourth = await CaptureAsync(scope, route, fetcher);
        clock.Advance(TimeSpan.FromHours(7));
        string[] fifth = await CaptureAsync(scope, route, fetcher);

        string[] early = ["11/14/runway-week-closing", "11/13/designer-market", "11/12/textile-open-call", "11/11/student-collections"];
        string[] late = ["11/10/atlas-workshop", "11/08/vintage-fair", "11/06/accessories-talk", "11/04/ikat-showcase"];
        ShouldReadTheListingAndOnly(third, early);
        ShouldReadTheListingAndOnly(fourth, late);
        ShouldReadTheListingAndOnly(fifth, early);
    }

    /// <summary>Checks a run read its listing first and then exactly the given articles; the admitted details are claimed in no set order.</summary>
    private static void ShouldReadTheListingAndOnly(string[] calls, params string[] articles)
    {
        calls[0].Should().Be(Listing);
        calls.Skip(1).Should().BeEquivalentTo(articles.Select(Article));
    }

    private static string Article(string path) => "https://www.afisha.uz/ru/fashion/2026/" + path;

    private static async Task<string> ReadListingAsync() => await File.ReadAllTextAsync(
        Path.Combine(AppContext.BaseDirectory, "Afisha", "Fixtures", "ru-fashion-listing.html"));

    private static async Task<ServiceProvider> BuildAsync(ReplayFetcher fetcher, TimeProvider clock)
    {
        string connectionString = await SourcePostgres.NewDatabaseAsync();
        var services = new ServiceCollection().AddLogging().AddSingleton(clock);
        services.AddHarvest().Configure(options => options.ConnectionString = connectionString);
        services.AddHarvestFetching();
        services.AddHarvestReaders().AddHarvestExtraction().AddHarvestPosts().AddHarvestDelivery();
        services.AddHarvestEngine();
        services.AddOcharoEventSources();
        services.AddSingleton<IPageFetcher>(fetcher);
        ServiceProvider provider = services.BuildServiceProvider(validateScopes: true);
        await provider.MigrateHarvestOnStartupAsync("afisha-section-replay");
        return provider;
    }

    private static async Task<(OcharoSetup Setup, OcharoSourceRegistration Route)> ProvisionAsync(AsyncServiceScope scope)
    {
        OcharoSetup setup = Value(await scope.ServiceProvider.GetRequiredService<OcharoSourceProvisioner>().EnsureAsync());
        return (setup, setup.Sources.First(item => item.SourceKey == "afisha-uz"));
    }

    /// <summary>Starts one capture run of the section, drains it and returns the URLs it fetched, in order.</summary>
    private static async Task<string[]> CaptureAsync(AsyncServiceScope scope, OcharoSourceRegistration route, ReplayFetcher fetcher)
    {
        int before = fetcher.Calls.Count;
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        var engine = scope.ServiceProvider.GetRequiredService<HarvestEngine>();
        RunModel queued = Value(await runs.StartAsync(route.PipelineId));
        int iterations = 0;
        while (await engine.ProcessNextAsync())
            if (++iterations > 20) throw new InvalidOperationException("The replayed frontier did not drain.");
        Value(await runs.GetAsync(queued.Id)).Pages.Should().Be(5);
        return [.. fetcher.Calls.Skip(before)];
    }

    private static T Value<T>(Result<T> result) where T : notnull => result is Result<T>.Success success
        ? success.Value : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);

    /// <summary>Serves the recorded listing and a synthetic event page for each fashion article; any other URL fails.</summary>
    private sealed class ReplayFetcher(string listing, TimeProvider? clock = null) : IPageFetcher
    {
        internal List<string> Calls { get; } = [];

        public Task<FetchResponse> FetchAsync(FetchRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add(request.Uri.AbsoluteUri);
            string path = request.Uri.AbsolutePath;
            string body = path == "/ru/fashion" ? listing
                : path.StartsWith("/ru/fashion/2026/", StringComparison.Ordinal) ? Event(path.Split('/')[^1])
                : throw new InvalidOperationException("Unexpected replay URL: " + request.Uri.AbsoluteUri);
            return Task.FromResult(new FetchResponse
            {
                Outcome = FetchOutcome.Ok, Usage = new() { Requests = 1, Bytes = Encoding.UTF8.GetByteCount(body) },
                Document = new FetchedDocument
                {
                    RequestUrl = request.Uri, FinalUrl = request.Uri, ContentType = "text/html", StatusCode = 200,
                    Body = Encoding.UTF8.GetBytes(body), FetchedAtUtc = clock?.GetUtcNow() ?? DateTimeOffset.UtcNow,
                },
            });
        }

        private static string Event(string slug)
        {
            string name = "Fixture " + slug.Replace('-', ' ');
            return "<html lang='ru'><article><h1>" + name + "</h1><p>A fashion event for designers.</p></article>"
                + "<script type='application/ld+json'>" + JsonSerializer.Serialize(new Dictionary<string, object>
                {
                    ["@type"] = "Event", ["name"] = name, ["startDate"] = "2026-11-21T17:00:00+05:00",
                }) + "</script></html>";
        }
    }
}
