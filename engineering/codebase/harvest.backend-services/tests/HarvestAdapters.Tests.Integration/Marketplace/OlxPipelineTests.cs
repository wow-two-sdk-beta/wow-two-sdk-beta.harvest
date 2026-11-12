using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Marketplace;

namespace HarvestAdapters.Tests.Integration.Marketplace;

/// <summary>Exercises filtered pagination, durable listing delivery and availability policy over PostgreSQL.</summary>
public sealed class OlxPipelineTests
{
    [Fact]
    public async Task Olx_ShouldTraverseFilteredPages_ArchiveListings_AndKeepUnknownChecksInconclusive()
    {
        string connectionString = await SourcePostgres.NewDatabaseAsync();
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));
        var fetcher = new FixtureFetcher(clock);
        var services = new ServiceCollection().AddLogging().AddSingleton<TimeProvider>(clock);
        services.AddHarvest().Configure(options => options.ConnectionString = connectionString);
        services.AddHarvestFetching();
        services.AddHarvestReaders().AddHarvestExtraction().AddHarvestPosts().AddHarvestDelivery();
        services.AddHarvestEngine();
        services.AddHarvestListingSources();
        services.AddSingleton<IPageFetcher>(fetcher);
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await provider.MigrateHarvestOnStartupAsync("olx-fixture");
        await using var scope = provider.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        Guid tenant = Value(await registry.RegisterTenantAsync(new TenantRegistration { Slug = "olx-test", Name = "OLX test" })).Id;
        Guid container = Value(await registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = tenant })).Id;
        Guid source = Value(await registry.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = container, Family = SourceFamily.Site, Handle = "www.olx.uz", Coverage = SourceCoverage.Filtered,
            Completeness = SourceCompleteness.Window, Posture = SourcePosture.OwnFetch, AiAllowed = false, PoolShareable = false,
            CheckPolicy = "web-direct",
        })).Id;
        Guid pipeline = Value(await registry.DeclarePipelineAsync(new PipelineDeclaration
        {
            ContainerId = container, SourceId = source, Name = "OLX fixture",
            Scope = new() { Url = "https://www.olx.uz/oz/elektronika/?q=desk&search%5Bfilter_float_price%3Ato%5D=10000000" },
            Limits = new() { MaxPages = 10, MaxItems = 10, MaxRequests = 20 },
        })).Id;
        var delivery = scope.ServiceProvider.GetRequiredService<IDeliveryBroker>();
        Guid subscription = Value(await delivery.SubscribeAsync(new SubscriptionDeclaration
        {
            TenantId = tenant, ContainerId = container, PipelineId = pipeline,
        })).Id;
        var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
        var posts = scope.ServiceProvider.GetRequiredService<IPostBroker>();
        var engine = scope.ServiceProvider.GetRequiredService<HarvestEngine>();
        Guid firstRun = Value(await runs.StartAsync(pipeline)).Id;
        await Drain(engine, clock);

        Value(await runs.GetAsync(firstRun)).Status.Should().Be(RunStatus.Completed);
        fetcher.Calls.Where(uri => !uri.AbsolutePath.Contains("/d/", StringComparison.Ordinal)).Should().HaveCount(2);
        Uri.UnescapeDataString(fetcher.Calls.Single(uri => uri.Query.Contains("page=2", StringComparison.Ordinal)).Query)
            .Should().Contain("q=desk").And.Contain("search[filter_float_price:to]=10000000");
        IReadOnlyList<PostModel> captured = Value(await posts.ListPostsAsync(container));
        captured.Should().HaveCount(2);
        captured.Should().OnlyContain(post => post.Base.Kind == PostKind.Listing);
        captured.SelectMany(post => post.Links).Select(link => link.ExternalKey).Should().BeEquivalentTo("Ab12", "Cd34");
        var delivered = Value(await delivery.PullAsync(subscription));
        delivered.Data.Should().HaveCount(2);
        delivered.Data.Should().OnlyContain(item => item.Type == "post.new");

        clock.Advance(TimeSpan.FromHours(7));
        fetcher.State = "unknown";
        Guid unknown = Value(await runs.StartAsync(pipeline, WorkKind.Check)).Id;
        await Drain(engine, clock);
        Value(await runs.GetAsync(unknown)).Failed.Should().Be(2);
        Value(await posts.ListPostsAsync(container)).Should().OnlyContain(post => post.State == PostState.Live && post.Links.Single().CheckCount == 1);

        fetcher.State = "inactive";
        Guid firstMissing = Value(await runs.StartAsync(pipeline, WorkKind.Check)).Id;
        await Drain(engine, clock);
        Value(await runs.GetAsync(firstMissing)).Status.Should().Be(RunStatus.Completed);
        Value(await posts.ListPostsAsync(container)).Should().OnlyContain(post => post.State == PostState.Suspect);

        clock.Advance(TimeSpan.FromHours(7));
        Value(await runs.StartAsync(pipeline, WorkKind.Check));
        await Drain(engine, clock);
        Value(await posts.ListPostsAsync(container)).Should().OnlyContain(post => post.State == PostState.Gone);

        clock.Advance(TimeSpan.FromHours(7));
        fetcher.State = "active";
        Value(await runs.StartAsync(pipeline, WorkKind.Check));
        await Drain(engine, clock);
        Value(await posts.ListPostsAsync(container)).Should().OnlyContain(post => post.State == PostState.Live && post.Links.Single().State == LinkState.Relisted);
    }

    private static async Task Drain(HarvestEngine engine, FakeTimeProvider clock)
    {
        for (int item = 0; item < 30; item++)
        {
            clock.Advance(TimeSpan.FromSeconds(3));
            if (!await engine.ProcessNextAsync()) break;
        }
    }

    private static T Value<T>(Result<T> result) where T : notnull
    {
        return result is Result<T>.Success success ? success.Value
            : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);
    }

    private sealed class FixtureFetcher(FakeTimeProvider clock) : IPageFetcher
    {
        internal List<Uri> Calls { get; } = [];
        internal string State { get; set; } = "active";
        public Task<FetchResponse> FetchAsync(FetchRequest request, CancellationToken cancellationToken = default)
        {
            Calls.Add(request.Uri);
            string html;
            if (!request.Uri.AbsolutePath.Contains("/d/", StringComparison.Ordinal))
            {
                bool second = request.Uri.Query.Contains("page=2", StringComparison.Ordinal);
                string id = second ? "Cd34" : "Ab12";
                html = $"<div data-testid='listing-grid'><a href='/d/obyavlenie/desk-ID{id}.html'>Desk</a></div>"
                    + (second ? "" : "<a rel='next' href='?page=2'>Next</a>");
            }
            else if (State == "unknown") html = "<h1>Unrecognized source layout</h1>";
            else if (State == "inactive") html = "<div data-testid='ad-inactive'>Объявление неактивно</div>";
            else html = "<div data-testid='offer_title'><h4>Desk</h4></div><div data-testid='ad_description'><div>Oak desk</div></div><div data-testid='ad-price-container'><h3>700 USD</h3></div>";
            return Task.FromResult(new FetchResponse
            {
                Outcome = FetchOutcome.Ok, Usage = new() { Requests = 1, Bytes = Encoding.UTF8.GetByteCount(html) },
                Document = new() { RequestUrl = request.Uri, FinalUrl = request.Uri, StatusCode = 200, ContentType = "text/html", Body = Encoding.UTF8.GetBytes(html), FetchedAtUtc = clock.GetUtcNow() },
            });
        }
    }
}
