using System.Text;
using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
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
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;

namespace HarvestAdapters.Tests.Integration.Telegram;

/// <summary>Runs one invented channel's preview through the real engine over PostgreSQL with a fixture fetcher: the page
/// chain, the page budget, the pace the engine asks for, and what an unchanged or an edited message does to its post.
/// A fixture run is no live access.</summary>
[Collection(SourceDeliveryCollection.Name)]
public sealed class TelegramPipelineTests
{
    private const string Newest = "https://t.me/s/demo_channel";

    [Fact]
    public async Task A_run_reads_the_newest_page_then_the_pages_before_it_and_never_more_than_three()
    {
        await using var fixture = await Fixture.StartAsync();
        fixture.Fetcher.Pages[Newest] = Page([18, 19, 20]);
        fixture.Fetcher.Pages[Newest + "?before=18"] = Page([15, 16, 17]);
        fixture.Fetcher.Pages[Newest + "?before=15"] = Page([12, 13, 14]);
        fixture.Fetcher.Pages[Newest + "?before=12"] = Page([9, 10, 11]);

        var run = await fixture.RunAsync();

        run.Status.Should().Be(RunStatus.Partial, "the third page is the run's budget, not the end of the channel's history");
        fixture.Fetcher.Requests.Select(request => request.Uri.AbsoluteUri).Should().Equal(Newest, Newest + "?before=18", Newest + "?before=15");
        fixture.Fetcher.Requests.Should().OnlyContain(request => request.MinDelay == TimeSpan.FromSeconds(10) && request.RobotsPolicy == RobotsPolicy.Obey);
        run.Captured.Should().Be(9);
        run.NewPosts.Should().Be(9);
        run.Pages.Should().Be(3);
        run.CompletionReason.Should().Be(RunReason.PageLimit, "older posts exist beyond the third page");
        var posts = Value(await fixture.Posts.ListPostsAsync(fixture.ContainerId, 100));
        posts.Should().HaveCount(9);
        posts.SelectMany(post => post.Links).Select(link => link.Url).Should().BeEquivalentTo(
            Enumerable.Range(12, 9).Select(id => $"https://t.me/demo_channel/{id}"), "every item credits its message permalink");
        posts.Should().OnlyContain(post => post.Base.PostedAt != null, "the message time is the post's publication time");
        posts.Should().OnlyContain(post => post.Base.Dates.Count == 0, "the publication time is never taken for an event date");
        (await fixture.HostKeys()).Should().BeEquivalentTo("t.me");
    }

    [Fact]
    public async Task An_empty_page_ends_the_run_after_one_request()
    {
        await using var fixture = await Fixture.StartAsync();
        fixture.Fetcher.Pages[Newest] = Page([]);

        var run = await fixture.RunAsync();

        run.Status.Should().Be(RunStatus.Completed);
        fixture.Fetcher.Requests.Should().ContainSingle();
        (run.Captured, run.Unchanged, run.Pages).Should().Be((0, 0, 1));
        Value(await fixture.Posts.ListPostsAsync(fixture.ContainerId, 100)).Should().BeEmpty();
    }

    [Fact]
    public async Task A_second_run_keeps_unchanged_messages_as_they_were_and_an_edit_is_the_only_new_revision()
    {
        await using var fixture = await Fixture.StartAsync();
        fixture.Fetcher.Pages[Newest] = Page([1, 2, 3]);
        fixture.Fetcher.Pages[Newest + "?before=1"] = Page([]);
        await fixture.RunAsync();
        var first = Value(await fixture.Posts.ListPostsAsync(fixture.ContainerId, 100));
        first.Should().OnlyContain(post => post.Revision == 1);

        // The same messages, with other view counts, a reaction and a later clock label; message 2 is edited.
        fixture.Fetcher.Pages[Newest] = Page([1, 2, 3], views: 4_000, edited: 2);
        var second = await fixture.RunAsync();

        (second.NewPosts, second.Captured, second.Unchanged).Should().Be((0, 1, 2), "counters make no revision, the edit makes one");
        var posts = Value(await fixture.Posts.ListPostsAsync(fixture.ContainerId, 100)).ToDictionary(post => post.Links.Single().Url);
        posts["https://t.me/demo_channel/1"].Revision.Should().Be(1);
        posts["https://t.me/demo_channel/3"].Revision.Should().Be(1);
        posts["https://t.me/demo_channel/2"].Revision.Should().Be(2);
        posts["https://t.me/demo_channel/2"].Base.Text.Should().Contain("revised notice 2");
    }

    [Fact]
    public async Task A_challenge_pauses_the_run_and_is_not_read_as_a_preview()
    {
        await using var fixture = await Fixture.StartAsync();
        fixture.Fetcher.Challenge = true;

        var run = await fixture.RunAsync();

        fixture.Fetcher.Requests.Should().ContainSingle("a challenge is not retried through another route");
        run.Captured.Should().Be(0);
        run.FetchOutcome.Should().Be(FetchOutcome.Challenge);
        run.Status.Should().NotBe(RunStatus.Completed);
        Value(await fixture.Posts.ListPostsAsync(fixture.ContainerId, 100)).Should().BeEmpty();
    }

    private static string Page(long[] ids, int views = 10, long edited = 0)
    {
        var messages = new StringBuilder();
        foreach (long id in ids)
        {
            string text = id == edited ? $"A revised notice {id}" : $"A synthetic notice {id}";
            string reactions = views > 10 ? "<div class=\"tgme_widget_message_reactions js-message_reactions\"><span class=\"tgme_reaction\"><b>👍</b>7</span></div>" : "";
            messages.Append($"""
                <div class="tgme_widget_message_wrap js-widget_message_wrap"><div class="tgme_widget_message text_not_supported_wrap js-widget_message" data-post="demo_channel/{id}">
                  <div class="tgme_widget_message_bubble">
                    <div class="tgme_widget_message_author accent_color"><a class="tgme_widget_message_owner_name" href="https://t.me/demo_channel"><span dir="auto">Demo Channel</span></a></div>
                    <div class="tgme_widget_message_text js-message_text" dir="auto">{text}</div>{reactions}
                    <div class="tgme_widget_message_footer compact js-message_footer"><div class="tgme_widget_message_info short js-message_info">
                      <span class="tgme_widget_message_views">{views}</span><span class="tgme_widget_message_meta">{(id == edited ? "edited &nbsp;" : "")}<a class="tgme_widget_message_date" href="https://t.me/demo_channel/{id}"><time datetime="2026-11-18T09:{id % 60:00}:00+00:00" class="time">09:{id % 60:00}</time></a></span>
                    </div></div>
                  </div>
                </div></div>
                """);
        }
        return $"""<html><body><main class="tgme_main"><div class="tgme_container"><section class="tgme_channel_history js-message_history">{messages}</section></div></main></body></html>""";
    }

    private static T Value<T>(Result<T> result) where T : notnull => result is Result<T>.Success success
        ? success.Value : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);

    private sealed class Fixture(ServiceProvider provider, AsyncServiceScope scope, PreviewFetcher fetcher, Guid containerId, Guid pipelineId) : IAsyncDisposable
    {
        internal PreviewFetcher Fetcher { get; } = fetcher;
        internal Guid ContainerId { get; } = containerId;
        internal IPostBroker Posts => scope.ServiceProvider.GetRequiredService<IPostBroker>();

        internal static async Task<Fixture> StartAsync()
        {
            var fetcher = new PreviewFetcher();
            string connectionString = await SourcePostgres.NewDatabaseAsync();
            var services = new ServiceCollection().AddLogging();
            services.AddHarvest().Configure(options => options.ConnectionString = connectionString);
            services.AddHarvestFetching();
            services.AddHarvestReaders().AddHarvestExtraction().AddHarvestPosts().AddHarvestDelivery();
            services.AddHarvestEngine();
            services.AddOcharoEventSources();
            services.AddSingleton<IPageFetcher>(fetcher);
            var provider = services.BuildServiceProvider(validateScopes: true);
            await provider.MigrateHarvestOnStartupAsync("telegram-fixture");
            var scope = provider.CreateAsyncScope();
            var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
            var tenant = Value(await registry.RegisterTenantAsync(new() { Slug = "telegram-fixture", Name = "Telegram fixture", PlatformCredentialsAllowed = false }));
            var container = Value(await registry.RegisterContainerAsync(new()
            {
                Kind = ContainerKind.Private, TenantId = tenant.Id, ExternalRef = "telegram-fixture", EmbeddingsEnabled = false,
            }));
            var source = Value(await registry.RegisterSourceAsync(new()
            {
                ContainerId = container.Id, Family = SourceFamily.Telegram, Handle = "demo_channel",
                Coverage = SourceCoverage.Filtered, Completeness = SourceCompleteness.Window, Posture = SourcePosture.OwnFetch,
                ModelProcessing = ModelProcessingMode.Off, PoolShareable = false, RobotsPolicy = RobotsPolicy.Obey, Authority = 80,
                CheckPolicy = "none",
            }));
            source.Handle.Should().Be("tg:demo_channel");
            var pipeline = Value(await registry.DeclarePipelineAsync(new()
            {
                ContainerId = container.Id, SourceId = source.Id, Scope = new() { Url = Newest }, Name = "Telegram · @demo_channel · Fixture",
                Limits = new() { MaxPages = 3, MaxItems = 150, MaxRequests = 180, MaxBytes = 30_000_000, MaxDuration = TimeSpan.FromMinutes(10) },
            }));
            return new(provider, scope, fetcher, container.Id, pipeline.Id);
        }

        internal async Task<RunModel> RunAsync()
        {
            var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
            var run = Value(await runs.StartAsync(pipelineId));
            var engine = scope.ServiceProvider.GetRequiredService<HarvestEngine>();
            int iterations = 0;
            while (await engine.ProcessNextAsync())
                if (++iterations > 12)
                    throw new InvalidOperationException("The fixture frontier did not drain.");
            return Value(await runs.GetAsync(run.Id));
        }

        internal async Task<IReadOnlyList<string>> HostKeys()
        {
            var runs = scope.ServiceProvider.GetRequiredService<IRunBroker>();
            return [.. Value(await runs.HostsAsync(ContainerId)).Select(host => host.HostKey).Distinct()];
        }

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await provider.DisposeAsync();
        }
    }

    private sealed class PreviewFetcher : IPageFetcher
    {
        internal List<FetchRequest> Requests { get; } = [];
        internal Dictionary<string, string> Pages { get; } = [];
        internal bool Challenge { get; set; }

        public Task<FetchResponse> FetchAsync(FetchRequest request, CancellationToken cancellationToken = default)
        {
            Requests.Add(request);
            if (Challenge)
                return Task.FromResult(new FetchResponse
                {
                    Outcome = FetchOutcome.Challenge, Usage = new() { Requests = 1 }, Message = "A challenge stood in the way.",
                });
            string body = Pages.TryGetValue(request.Uri.AbsoluteUri, out string? page) ? page
                : throw new InvalidOperationException("Unexpected fixture URL: " + request.Uri.AbsoluteUri);
            return Task.FromResult(new FetchResponse
            {
                Outcome = FetchOutcome.Ok, Usage = new() { Requests = 1, Bytes = Encoding.UTF8.GetByteCount(body) },
                Document = new FetchedDocument
                {
                    RequestUrl = request.Uri, FinalUrl = request.Uri, ContentType = "text/html; charset=utf-8", StatusCode = 200,
                    Body = Encoding.UTF8.GetBytes(body), FetchedAtUtc = DateTimeOffset.UtcNow,
                },
            });
        }
    }
}
