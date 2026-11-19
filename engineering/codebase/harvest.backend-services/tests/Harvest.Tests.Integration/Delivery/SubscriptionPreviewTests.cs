using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Harvest.Tests.Integration.Posts;
using Harvest.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

namespace Harvest.Tests.Integration.Delivery;

[Collection("Post delivery transactions")]
public sealed class SubscriptionPreviewTests
{
    private const string Beauty = "style-fashion/beauty";

    [Fact]
    public async Task Preview_ShouldCountMatchedNoMatchAndPending_InTheWindow()
    {
        await using var host = await PreviewHost.StartAsync();
        await host.SeedAsync(host.SourceId, "last-season", "Last season's fair", PostKind.Event, TagsFor("event", "style-fashion/high-fashion"));
        host.Clock.Advance(TimeSpan.FromDays(40));
        var fashion = await host.SeedAsync(host.SourceId, "fashion-week", "Fashion week", PostKind.Event,
            TagsFor("event", "style-fashion/high-fashion"), titleEn: "Tashkent Fashion Week");
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await host.SeedAsync(host.SourceId, "beauty-expo", "Beauty expo", PostKind.Event, TagsFor("event", $"{Beauty}/skin-care", "cosmetics"));
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await host.SeedAsync(host.SourceId, "untagged", "A fresh post with no answer yet", PostKind.Event);

        var preview = PostHost.Value(await host.Preview.PreviewAsync(host.Request(Fashion())));

        preview.SinceUtc.Should().Be(host.Clock.GetUtcNow().AddDays(-SelectorRules.DefaultWindowDays));
        preview.Considered.Should().Be(3);
        preview.Tagged.Should().Be(2);
        preview.Matched.Should().Be(1);
        preview.NoMatch.Should().Be(1);
        preview.Pending.Should().Be(1);
        preview.Truncated.Should().BeFalse();
        var sample = preview.Samples.Should().ContainSingle().Subject;
        sample.PostId.Should().Be(fashion);
        sample.Revision.Should().Be(1);
        sample.Verdict.Should().Be(SelectorVerdict.Match);
        sample.Clauses.Should().Equal("type:event", "domain:style-fashion");
        sample.Title.Should().Be("Fashion week");
        sample.TitleEn.Should().Be("Tashkent Fashion Week");
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_subscriptions")).Should().Be(0);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_subscription_event")).Should().Be(0);
    }

    [Fact]
    public async Task Preview_ShouldCountWhatEachClauseAddsAndRemoves()
    {
        await using var host = await PreviewHost.StartAsync();
        await SeedClauseCasesAsync(host);

        var preview = PostHost.Value(await host.Preview.PreviewAsync(host.Request(Fashion())));

        preview.Considered.Should().Be(5);
        preview.Matched.Should().Be(2);
        preview.NoMatch.Should().Be(3);
        preview.Clauses.Should().Equal(
            new PreviewClauseCount { Clause = "type", Removes = 1 },
            new PreviewClauseCount { Clause = "domain:style-fashion", Adds = 1 },
            new PreviewClauseCount { Clause = "topic:fashion-show", Adds = 1 },
            new PreviewClauseCount { Clause = $"except:{Beauty}", Removes = 1 });
        var limited = PostHost.Value(await host.Preview.PreviewAsync(host.Request(Fashion()) with { Samples = 1 }));
        limited.Samples.Should().ContainSingle().Which.Clauses.Should().Equal("type:event", "topic:fashion-show");
        var none = PostHost.Value(await host.Preview.PreviewAsync(host.Request(Fashion()) with { Samples = 0 }));
        none.Samples.Should().BeEmpty();
        none.Matched.Should().Be(2);
    }

    [Fact]
    public async Task Preview_ShouldListNearMisses_WithTheRelaxationThatAdmitsThem()
    {
        await using var host = await PreviewHost.StartAsync();
        await SeedClauseCasesAsync(host);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var unsure = await host.SeedAsync(host.SourceId, "unsure", "An unsure post", PostKind.Event, TagsFor(null, "style-fashion/high-fashion"));

        var preview = PostHost.Value(await host.Preview.PreviewAsync(host.Request(Fashion())));

        preview.Pending.Should().Be(1);
        preview.NearMisses.Select(post => post.Title).Should().Equal("A listing", "Beauty expo");
        preview.NearMisses.Select(post => string.Join(',', post.Clauses)).Should().Equal("type", $"except:{Beauty}");
        preview.NearMisses.Should().OnlyContain(post => post.Verdict == SelectorVerdict.NoMatch);
        preview.NearMisses.Select(post => post.PostId).Should().NotContain(unsure);
        preview.NearMisses.Select(post => post.Title).Should().NotContain("A marathon");
    }

    [Fact]
    public async Task Preview_ShouldCountWhatEachVetoRemoves_ForDomainTopicAndTimeframe()
    {
        await using var host = await PreviewHost.StartAsync();
        await host.SeedAsync(host.SourceId, "past-show", "A past show", PostKind.Event, [.. TagsFor("event", "style-fashion/high-fashion"), "timeframe:past"]);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await host.SeedAsync(host.SourceId, "beauty-expo", "Beauty expo", PostKind.Event, TagsFor("event", $"{Beauty}/skin-care"));
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await host.SeedAsync(host.SourceId, "marathon-show", "A fashion marathon", PostKind.Event, TagsFor("event", "style-fashion/high-fashion", "marathon"));
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await host.SeedAsync(host.SourceId, "upcoming-show", "An upcoming show", PostKind.Event, [.. TagsFor("event", "style-fashion/high-fashion"), "timeframe:upcoming"]);
        var selector = new SubscriptionSelector
        {
            EntityTypes = ["event"], Domains = [new SelectorDomain { Key = "style-fashion" }],
            ExcludeDomains = [Beauty], ExcludeTopics = ["marathon"], ExcludeTimeframes = ["past"],
        };

        var preview = PostHost.Value(await host.Preview.PreviewAsync(host.Request(selector)));

        preview.Matched.Should().Be(1);
        preview.NoMatch.Should().Be(3);
        preview.Clauses.Should().Equal(
            new PreviewClauseCount { Clause = "type" },
            new PreviewClauseCount { Clause = "domain:style-fashion", Adds = 1 },
            new PreviewClauseCount { Clause = $"exclude-domain:{Beauty}", Removes = 1 },
            new PreviewClauseCount { Clause = "exclude-topic:marathon", Removes = 1 },
            new PreviewClauseCount { Clause = "exclude-timeframe:past", Removes = 1 });
        preview.NearMisses.Select(post => string.Join(',', post.Clauses)).Should().Equal("exclude-topic:marathon", $"exclude-domain:{Beauty}", "exclude-timeframe:past");
    }

    [Fact]
    public async Task Preview_ShouldBreakCountsDownBySource()
    {
        await using var host = await PreviewHost.StartAsync();
        var group = PostHost.Value(await host.Registry.DeclareSourceGroupAsync(new SourceGroupDeclaration { ContainerId = host.ContainerId, Name = "Publisher A" }));
        var sourceA = await host.SourceAsync("a.example");
        PostHost.Value(await host.Registry.UpdateSourceAsync(sourceA, new SourceUpdate { Name = "Source A", GroupId = group.Id }));
        var sourceB = await host.SourceAsync("b.example");
        await host.SeedAsync(sourceA, "a-fashion", "A fashion fair", PostKind.Event, TagsFor("event", "style-fashion/high-fashion"));
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await host.SeedAsync(sourceA, "a-untagged", "An untagged fair", PostKind.Event);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await host.SeedAsync(sourceB, "b-fashion", "Another fashion fair", PostKind.Event, TagsFor("event", "style-fashion/womens-fashion"));

        var all = PostHost.Value(await host.Preview.PreviewAsync(host.Request(Fashion())));

        all.Sources.Should().Equal(
            new PreviewSourceCount { SourceId = sourceA, Name = "Source A", GroupId = group.Id, Considered = 2, Matched = 1, Pending = 1 },
            new PreviewSourceCount { SourceId = sourceB, Considered = 1, Matched = 1 });

        var groupOnly = PostHost.Value(await host.Preview.PreviewAsync(host.Request(Fashion() with
        {
            Sources = new SelectorSources { Include = [new SelectorSourceRef { GroupId = group.Id }] },
        })));
        groupOnly.Considered.Should().Be(3);
        groupOnly.NoMatch.Should().Be(1);
        groupOnly.Sources.Select(row => row.SourceId).Should().Equal(sourceA);

        var withoutA = PostHost.Value(await host.Preview.PreviewAsync(host.Request(Fashion() with
        {
            Sources = new SelectorSources { Exclude = [new SelectorSourceRef { SourceId = sourceA }] },
        })));
        withoutA.Matched.Should().Be(1);
        withoutA.Sources.Should().ContainSingle().Which.SourceId.Should().Be(sourceB);
    }

    [Fact]
    public async Task Preview_ShouldRefuseAForeignContainer_AndAWindowOverAYear()
    {
        await using var host = await PreviewHost.StartAsync();
        var other = PostHost.Value(await host.Registry.RegisterTenantAsync(new TenantRegistration { Slug = "other", Name = "Other" }));
        var foreign = PostHost.Value(await host.Registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = other.Id }));
        var own = PostHost.Value(await host.Registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = host.TenantId }));
        var now = host.Clock.GetUtcNow();

        Refused(await host.Preview.PreviewAsync(host.Request(Fashion()) with { ContainerId = foreign.Id }), AppErrorType.NotFound);
        Refused(await host.Preview.PreviewAsync(host.Request(Fashion()) with { ContainerId = Guid.NewGuid() }), AppErrorType.NotFound);
        Refused(await host.Preview.PreviewAsync(host.Request(Fashion()) with { TenantId = Guid.NewGuid() }), AppErrorType.NotFound);
        Refused(await host.Preview.PreviewAsync(host.Request(Fashion()) with { SinceUtc = now.AddDays(-SelectorRules.MaxWindowDays).AddMinutes(-1) }), AppErrorType.Validation);
        Refused(await host.Preview.PreviewAsync(host.Request(Fashion()) with { SinceUtc = now.AddMinutes(1) }), AppErrorType.Validation);
        Refused(await host.Preview.PreviewAsync(host.Request(Fashion()) with { Samples = 51 }), AppErrorType.Validation);
        Refused(await host.Preview.PreviewAsync(host.Request(Fashion()) with { Samples = -1 }), AppErrorType.Validation);
        Refused(await host.Preview.PreviewAsync(host.Request(new SubscriptionSelector { EntityTypes = ["concert"] })), AppErrorType.Validation);

        PostHost.Value(await host.Preview.PreviewAsync(host.Request(Fashion()) with { SinceUtc = now.AddDays(-SelectorRules.MaxWindowDays) }))
            .Considered.Should().Be(0);
        PostHost.Value(await host.Preview.PreviewAsync(host.Request(Fashion()) with { ContainerId = own.Id })).Considered.Should().Be(0);
    }

    [Fact]
    public async Task Preview_ShouldRefuseUnknownTaxonomyKeys_AndForeignRefs()
    {
        await using var host = await PreviewHost.StartAsync();
        var other = PostHost.Value(await host.Registry.RegisterTenantAsync(new TenantRegistration { Slug = "other", Name = "Other" }));
        var foreign = PostHost.Value(await host.Registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = other.Id }));
        var foreignSource = PostHost.Value(await host.Registry.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = foreign.Id, Family = SourceFamily.Site, Handle = "foreign.example", Coverage = SourceCoverage.Whole,
            Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch,
        })).Id;

        Refused(await host.Preview.PreviewAsync(host.Request(new SubscriptionSelector
        {
            Domains = [new SelectorDomain { Key = "style-fashion/no-such-node" }],
        })), AppErrorType.Validation);
        Refused(await host.Preview.PreviewAsync(host.Request(new SubscriptionSelector
        {
            Domains = [new SelectorDomain { Key = "style-fashion", Except = ["style-fashion/no-such-node"] }],
        })), AppErrorType.Validation);
        Refused(await host.Preview.PreviewAsync(host.Request(new SubscriptionSelector { ExcludeDomains = ["no-such-domain"] })), AppErrorType.Validation);
        Refused(await host.Preview.PreviewAsync(host.Request(Fashion() with
        {
            Sources = new SelectorSources { Include = [new SelectorSourceRef { SourceId = foreignSource }] },
        })), AppErrorType.Validation);
        Refused(await host.Preview.PreviewAsync(host.Request(Fashion() with
        {
            Sources = new SelectorSources { Exclude = [new SelectorSourceRef { GroupId = Guid.NewGuid() }] },
        })), AppErrorType.Validation);
        Refused(await host.Preview.PreviewAsync(host.Request(Fashion() with
        {
            Sources = new SelectorSources { Include = [new SelectorSourceRef { PipelineId = Guid.NewGuid() }] },
        })), AppErrorType.Validation);

        PostHost.Value(await host.Preview.PreviewAsync(host.Request(Fashion() with
        {
            Sources = new SelectorSources { Include = [new SelectorSourceRef { PipelineId = host.PipelineId }, new SelectorSourceRef { SourceId = host.SourceId }] },
        }))).Considered.Should().Be(0);
    }

    [Fact]
    public async Task Preview_ShouldJudgeAtMostFiveThousandPosts_AndSayTruncated()
    {
        await using var host = await PreviewHost.StartAsync();
        var envelope = StoreJson.Write(PreviewHost.Envelope("Bulk", "Bulk text", PostKind.Event, TagsFor("event", "style-fashion/high-fashion"), null));
        await host.ExecuteAsync("""
            INSERT INTO harvest_posts(id,container_id,kind,state,state_since,base,field_prov,first_seen_at,last_changed_at)
            SELECT gen_random_uuid(),@container,'event','live',@now,CAST(@base AS jsonb),'{}',@now,@now - make_interval(secs => g)
            FROM generate_series(1,5001) g
            """, ("container", host.ContainerId), ("now", host.Clock.GetUtcNow()), ("base", envelope));

        var preview = PostHost.Value(await host.Preview.PreviewAsync(host.Request(Fashion())));

        preview.Considered.Should().Be(5000);
        preview.Truncated.Should().BeTrue();
        var narrow = PostHost.Value(await host.Preview.PreviewAsync(host.Request(Fashion()) with { SinceUtc = host.Clock.GetUtcNow().AddSeconds(-10) }));
        narrow.Considered.Should().Be(10);
        narrow.Truncated.Should().BeFalse();
    }

    /// <summary>Seeds, oldest to newest, a marathon, a beauty expo, a high-fashion event, a beauty expo with a fashion show
    /// and a listing, all tagged as Layer 1 would.</summary>
    private static async Task SeedClauseCasesAsync(PreviewHost host)
    {
        await host.SeedAsync(host.SourceId, "marathon", "A marathon", PostKind.Event, TagsFor("event", "sports/running", "marathon"));
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await host.SeedAsync(host.SourceId, "beauty-expo", "Beauty expo", PostKind.Event, TagsFor("event", $"{Beauty}/skin-care", "cosmetics", "skin-care"));
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await host.SeedAsync(host.SourceId, "high-fashion", "High fashion event", PostKind.Event, TagsFor("event", "style-fashion/high-fashion"));
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await host.SeedAsync(host.SourceId, "beauty-show", "Beauty show", PostKind.Event, TagsFor("event", $"{Beauty}/makeup-and-accessories", "cosmetics", "fashion-show"));
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        await host.SeedAsync(host.SourceId, "listing", "A listing", PostKind.Event, TagsFor("listing", "style-fashion/high-fashion"));
    }

    private static SubscriptionSelector Fashion() => new()
    {
        EntityTypes = ["event"],
        Domains = [new SelectorDomain { Key = "style-fashion", Except = [Beauty] }],
        Topics = ["fashion-show"],
    };

    /// <summary>Lists the Layer 1 tags an answer writes: the type unless it is null, each domain node with its ancestors,
    /// then the topics.</summary>
    private static IReadOnlyList<string> TagsFor(string? type, string domain, params string[] topics) =>
    [
        .. type is null ? [] : new[] { "type:" + type },
        .. PostTagsOverlay.Lineage(domain).Select(node => "domain:" + node),
        .. topics.Select(topic => "topic:" + topic),
    ];

    private static void Refused(Result<SubscriptionPreviewModel> result, AppErrorType type) =>
        result.Should().BeOfType<Result<SubscriptionPreviewModel>.Failure>().Which.Error.Type.Should().Be(type);

    /// <summary>The module wired as a Form 1 consumer wires it, with the preview, over a fresh database.</summary>
    private sealed class PreviewHost(ServiceProvider services, AsyncServiceScope scope, string connectionString, FakeTimeProvider clock) : IAsyncDisposable
    {
        internal FakeTimeProvider Clock { get; } = clock;
        internal IPostBroker Posts => scope.ServiceProvider.GetRequiredService<IPostBroker>();
        internal IRegistrationBroker Registry => scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        internal ISubscriptionPreviewBroker Preview => scope.ServiceProvider.GetRequiredService<ISubscriptionPreviewBroker>();
        internal Guid ContainerId { get; private set; }
        internal Guid TenantId { get; private set; }
        internal Guid SourceId { get; private set; }
        internal Guid PipelineId { get; private set; }

        internal static async Task<PreviewHost> StartAsync()
        {
            var connectionString = (await PostgresServer.SharedAsync()).NewDatabase("preview");
            var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));
            var registration = new ServiceCollection().AddLogging().AddSingleton<TimeProvider>(clock);
            registration.AddHarvest().Configure(options => options.ConnectionString = connectionString);
            registration.AddHarvestPosts().AddHarvestSubscriptionPreview();
            var provider = registration.BuildServiceProvider(validateScopes: true);
            await provider.MigrateHarvestOnStartupAsync("test");
            var host = new PreviewHost(provider, provider.CreateAsyncScope(), connectionString, clock);
            host.TenantId = PostHost.Value(await host.Registry.RegisterTenantAsync(new TenantRegistration { Slug = "events", Name = "Events" })).Id;
            host.ContainerId = PostHost.Value(await host.Registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Pool })).Id;
            host.SourceId = await host.SourceAsync("mstar.uz");
            host.PipelineId = PostHost.Value(await host.Registry.DeclarePipelineAsync(new PipelineDeclaration
            {
                ContainerId = host.ContainerId, SourceId = host.SourceId,
                Schedules = [new ScheduleDeclaration { Kind = WorkKind.Capture, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(1) }],
            })).Id;
            return host;
        }

        internal SubscriptionPreviewRequest Request(SubscriptionSelector selector) =>
            new() { TenantId = TenantId, ContainerId = ContainerId, Selector = selector };

        internal async Task<Guid> SourceAsync(string handle) =>
            PostHost.Value(await Registry.RegisterSourceAsync(new SourceRegistration
            {
                ContainerId = ContainerId, Family = SourceFamily.Site, Handle = handle, Coverage = SourceCoverage.Whole,
                Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch,
            })).Id;

        /// <summary>Saves one capture whose envelope carries the Layer 1 tags, as a resolved answer would.</summary>
        internal async Task<Guid> SeedAsync(Guid sourceId, string key, string title, PostKind kind, IReadOnlyList<string>? tags = null, string? titleEn = null)
        {
            var capture = new Capture
            {
                ExternalKey = key, Url = $"https://seed.example/{key}", RequestUrl = $"https://seed.example/{key}", FinalUrl = $"https://seed.example/{key}",
                CapturedAtUtc = Clock.GetUtcNow(), Body = Encoding.UTF8.GetBytes($"{title} {key}"), ContentProjection = $"{title} {key}",
                HttpStatus = 200, ContentType = "text/html",
            };
            var saved = PostHost.Value(await Posts.SaveCaptureAsync(ContainerId, sourceId, null, capture,
                Envelope(title, capture.ContentProjection, kind, tags ?? [], titleEn)));
            return saved.Post.Id;
        }

        internal static PostEnvelope Envelope(string title, string text, PostKind kind, IReadOnlyList<string> tags, string? titleEn) => new()
        {
            Kind = kind, Title = title, Text = text, Language = new PostLanguage { Code = "en" },
            Tags = [.. tags.Select(tag => new PostTag { Tag = tag, Score = 0.9, Tier = ExtractionTier.SmallModel })],
            Extras = titleEn is null ? new Dictionary<string, JsonElement>()
                : new Dictionary<string, JsonElement> { [PostTagsConstants.ExtrasNamespace] = JsonSerializer.SerializeToElement(new { titleEn }) },
            Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
        };

        internal async Task<T> ScalarAsync<T>(string sql)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            return (T)(await command.ExecuteScalarAsync())!;
        }

        internal async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand(sql, connection);
            foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
            await command.ExecuteNonQueryAsync();
        }

        public async ValueTask DisposeAsync()
        {
            await scope.DisposeAsync();
            await services.DisposeAsync();
        }
    }
}
