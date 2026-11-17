using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
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

    private static T Value<T>(Result<T> result) where T : notnull => result is Result<T>.Success success
        ? success.Value : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);

    /// <summary>Serves the recorded listing and a synthetic event page for each fashion article; any other URL fails.</summary>
    private sealed class ReplayFetcher(string listing) : IPageFetcher
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
                    Body = Encoding.UTF8.GetBytes(body), FetchedAtUtc = DateTimeOffset.UtcNow,
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
