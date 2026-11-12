using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;

namespace HarvestAdapters.Tests.Integration.Ocharo;

public sealed class OcharoPipelineTests
{
    [Fact]
    public async Task Instagram_flows_through_the_engine_into_review_delivery_without_turning_post_time_into_event_time()
    {
        string connectionString = await SourcePostgres.NewDatabaseAsync();
        var services = new ServiceCollection().AddLogging();
        services.AddHarvest().Configure(options => options.ConnectionString = connectionString);
        services.AddHarvestFetching();
        services.AddHarvestReaders().AddHarvestExtraction().AddHarvestPosts().AddHarvestDelivery();
        services.AddHarvestEngine();
        services.AddOcharoEventSources();
        services.AddHarvestInstagram();
        services.AddSingleton<IInstagramMediaClient, InstagramFixtureClient>();
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await provider.MigrateHarvestOnStartupAsync("instagram-fixture");
        await using var scope = provider.CreateAsyncScope();
        OcharoSetup setup = Value(await scope.ServiceProvider.GetRequiredService<OcharoSourceProvisioner>().EnsureAsync(includeInstagram: true));
        var route = setup.Sources.First(item => item.Seed == "https://www.instagram.com/fwtashkent/");
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        var run = Value(await runs.StartAsync(route.PipelineId));
        (await scope.ServiceProvider.GetRequiredService<HarvestEngine>().ProcessNextAsync()).Should().BeTrue();
        Value(await runs.GetAsync(run.Id)).Status.Should().Be(RunStatus.Completed);
        var page = Value(await scope.ServiceProvider.GetRequiredService<IDeliveryBroker>().PullAsync(route.SubscriptionId));
        page.Data.Should().ContainSingle();
        JsonElement data = page.Data[0].Data!.Value;
        data.GetProperty("title").GetString().Should().Contain("Fashion Week");
        (data.TryGetProperty("starts", out var starts) && starts.ValueKind != JsonValueKind.Null).Should().BeFalse();
        data.GetProperty("links").EnumerateArray().Should().Contain(link => link.GetProperty("url").GetString() == "https://www.instagram.com/p/Fixture123/");
        data.GetProperty("schemaOrg").GetProperty("instagram").GetProperty("requiresReview").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Afisha_flows_through_public_engine_into_filtered_durable_delivery()
    {
        var fetcher = new FixtureFetcher();
        string connectionString = await SourcePostgres.NewDatabaseAsync();
        var services = new ServiceCollection().AddLogging();
        services.AddHarvest().Configure(options => options.ConnectionString = connectionString);
        services.AddHarvestFetching();
        services.AddHarvestReaders().AddHarvestExtraction().AddHarvestPosts().AddHarvestDelivery();
        services.AddHarvestEngine();
        services.AddOcharoEventSources();
        services.AddSingleton<IPageFetcher>(fetcher);
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await provider.MigrateHarvestOnStartupAsync("ocharo-fixture");
        await using var scope = provider.CreateAsyncScope();
        OcharoSetup setup = Value(await scope.ServiceProvider.GetRequiredService<OcharoSourceProvisioner>().EnsureAsync());
        OcharoSourceRegistration route = setup.Sources.First(item => item.SourceKey == "afisha-uz");
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        var delivery = scope.ServiceProvider.GetRequiredService<IDeliveryBroker>();
        var posts = scope.ServiceProvider.GetRequiredService<IPostBroker>();
        RunModel run = Value(await runs.StartAsync(route.PipelineId));
        var engine = scope.ServiceProvider.GetRequiredService<HarvestEngine>();
        int iterations = 0;
        while (await engine.ProcessNextAsync())
            if (++iterations > 12) throw new InvalidOperationException("Fixture frontier did not drain.");
        Value(await runs.GetAsync(run.Id)).Status.Should().Be(RunStatus.Completed);
        var rawPosts = Value(await posts.ListPostsAsync(setup.ContainerId));
        rawPosts.Should().HaveCount(2);
        rawPosts.Select(post => post.Base.Title).Should().Contain("Evening concert");
        CursorPage<DeliveryEvent> page = Value(await delivery.PullAsync(route.SubscriptionId));
        page.Data.Should().ContainSingle();
        page.Data[0].Context["catalog"].Should().Be("ocharo-fashion");
        JsonElement data = page.Data[0].Data!.Value;
        data.GetProperty("schema").GetString().Should().Be("wow2.event.v1");
        data.GetProperty("title").GetString().Should().Be("Fashline fashion show");
        data.GetProperty("starts").GetProperty("value").GetString().Should().StartWith("2026-11-21");
        data.GetProperty("contentLinks").EnumerateArray().Should().Contain(link =>
            link.GetProperty("url").GetString() == "https://registration.example.test/apply?edition=2026");
        var replay = Value(await delivery.PullAsync(route.SubscriptionId));
        replay.Data[0].EventId.Should().Be(page.Data[0].EventId);
        Value(await delivery.PullAsync(route.SubscriptionId, page.Next)).Data.Should().BeEmpty();
        fetcher.Calls.Should().HaveCount(3);
        fetcher.Calls.Should().NotContain(url => url.Contains("/api/"));
    }

    private sealed class InstagramFixtureClient : IInstagramMediaClient
    {
        public Task<InstagramMediaPage> ReadAsync(string username, string? after = null, CancellationToken cancellationToken = default) =>
            Task.FromResult(new InstagramMediaPage
            {
                Outcome = FetchOutcome.Ok, Usage = new() { Requests = 1, Bytes = 200 },
                Media = [JsonSerializer.SerializeToElement(new
                {
                    id = "17890000000000001", caption = "Open call for designers: Fashion Week Tashkent. Applications open.",
                    timestamp = "2026-10-27T10:00:00+0000", permalink = "https://www.instagram.com/p/Fixture123/", media_type = "IMAGE",
                })],
            });
    }

    private static T Value<T>(Result<T> result) where T : notnull => result is Result<T>.Success success
        ? success.Value : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);

    private sealed class FixtureFetcher : IPageFetcher
    {
        internal List<string> Calls { get; } = [];
        public Task<FetchResponse> FetchAsync(FetchRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add(request.Uri.AbsoluteUri);
            string body = request.Uri.AbsolutePath switch
            {
                "/ru/fashion" => "<a href='/ru/fashion/2026/10/14/fashline'>Fashion</a><a href='/ru/concerts/2026/10/14/concert'>Concert</a>",
                "/ru/fashion/2026/10/14/fashline" => Event("Fashline fashion show", "A fashion runway event for designers."),
                "/ru/concerts/2026/10/14/concert" => Event("Evening concert", "A live music concert."),
                _ => throw new InvalidOperationException("Unexpected fixture URL: " + request.Uri.AbsoluteUri),
            };
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
        private static string Event(string name, string description) => "<html lang='ru'><article><h1>" + name
            + "</h1><p>" + description + "</p><a href='https://registration.example.test/apply?edition=2026'>Apply</a></article><script type='application/ld+json'>"
            + JsonSerializer.Serialize(new Dictionary<string, object>
            {
                ["@type"] = "Event", ["name"] = name, ["description"] = description,
                ["startDate"] = "2026-11-21T17:00:00+05:00",
            }) + "</script></html>";
    }
}
