using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Harvest.Tests.Integration.Posts;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

namespace Harvest.Tests.Integration.Delivery;

/// <summary>The cutover operations over PostgreSQL: the diff of a selector against a registered filter, the explanation of one
/// post's verdict, and the adoption of older subscriptions' posts. A test filter keyed <c>test-fashion-v1</c> stands in for
/// Ocharo's; posts carry their Layer 1 tags in the capture's envelope.</summary>
[Collection("Post delivery transactions")]
public sealed class SubscriptionOperationsTests
{
    private const string FilterKey = "test-fashion-v1";

    private static readonly string[] FashionTags = ["type:event", "domain:style-fashion/high-fashion", "domain:style-fashion", "topic:fashion-week"];
    private static readonly string[] SportsTags = ["type:event", "domain:sports", "topic:marathon"];

    private static readonly SubscriptionSelector Fashion = new() { EntityTypes = ["event"], Domains = [new SelectorDomain { Key = "style-fashion" }] };
    private static readonly SubscriptionSelector Sports = new() { EntityTypes = ["event"], Domains = [new SelectorDomain { Key = "sports" }] };

    /// <summary>Ocharo's selector in miniature: events under style-fashion except beauty, or under the apparel industry, or on a
    /// clothing topic.</summary>
    private static SubscriptionSelector OcharoLike(Guid sourceId) => new()
    {
        EntityTypes = ["event"],
        Domains =
        [
            new SelectorDomain { Key = "style-fashion", Except = ["style-fashion/beauty"] },
            new SelectorDomain { Key = "business-and-finance/industries/apparel-industry" },
        ],
        Topics = ["fashion-show", "textile"],
        Sources = new SelectorSources { Include = [new SelectorSourceRef { SourceId = sourceId }] },
    };

    [Fact]
    public async Task Diff_ShouldCountBothSides_AndListEachDisagreement()
    {
        await using var host = await PostHost.StartAsync();
        await using var scope = Open(host);
        var second = await host.SourceAsync("second.uz");
        var subscription = PostHost.Value(await scope.Delivery.SubscribeAsync(Declaration(host, OcharoLike(host.SourceId))));

        var week = await SaveAsync(host, scope, "week", "Tashkent Fashion Week", "A fashion week.", FashionTags);
        var build = await SaveAsync(host, scope, "build", "Build & Co", "Стройматериалы, аксессуары для дома: аксессуар к каждому заказу.",
            ["type:event", "domain:business-and-finance", "topic:building-materials"]);
        var beauty = await SaveAsync(host, scope, "beauty", "Beauty and fashion expo", "Cosmetics and skin care.",
            ["type:event", "domain:style-fashion", "domain:style-fashion/beauty", "topic:cosmetics", "topic:skin-care", "timeframe:upcoming"]);
        var runway = await SaveAsync(host, scope, "runway", "Beauty expo with a runway", "Cosmetics and a show.",
            ["type:event", "domain:style-fashion", "domain:style-fashion/beauty", "topic:cosmetics", "topic:fashion-show"]);
        var textile = await SaveAsync(host, scope, "textile", "Texworld", "A textile trade fair.",
            ["type:event", "domain:business-and-finance/industries/apparel-industry", "domain:business-and-finance/industries", "domain:business-and-finance", "topic:textile"]);
        var marathon = await SaveAsync(host, scope, "marathon", "City marathon", "A marathon.", SportsTags);
        var waiting = await SaveAsync(host, scope, "waiting", "Fashion news", "Not tagged yet.");
        await SaveAsync(host, scope, "outside", "Fashion fair elsewhere", "A fashion fair.", FashionTags, second, "second.uz");
        var failing = await SaveAsync(host, scope, "failing", "Fashion explode", "The filter throws on this one.", FashionTags);

        var diff = PostHost.Value(await scope.Operations.DiffAsync(new SelectorDiffRequest { SubscriptionId = subscription.Id, FilterKey = FilterKey }));

        diff.Subscription.Should().Be("ocharo-events");
        diff.SelectorVersion.Should().Be(1);
        diff.FilterKey.Should().Be(FilterKey);
        diff.SinceUtc.Should().Be(host.Clock.GetUtcNow().AddDays(-7));
        (diff.Posts, diff.OutOfScope, diff.Both, diff.FilterOnly, diff.SelectorOnly, diff.Neither, diff.Pending, diff.FilterUndecided)
            .Should().Be((9, 1, 1, 2, 2, 1, 1, 1));
        var expected = InPostOrder(build, beauty).Concat(InPostOrder(runway, textile)).ToArray();
        diff.Items.Select(item => item.PostId).Should().Equal(expected, "the filter-only posts come first, then the selector-only ones, each by post id");

        var caex = diff.Items.Single(item => item.PostId == build.Post.Id);
        caex.Filter.Should().Be("yes");
        caex.Selector.Should().Be(SelectorVerdict.NoMatch);
        caex.Clauses.Should().Equal("no-subject");
        caex.Title.Should().Be("Build & Co");
        caex.Source.Should().Be("site/mstar.uz");
        caex.EntityType.Should().Be("event");
        caex.Domains.Should().Equal("business-and-finance");
        caex.Topics.Should().Equal("building-materials");
        caex.Timeframe.Should().BeNull();
        var held = diff.Items.Single(item => item.PostId == beauty.Post.Id);
        (held.Filter, held.Selector).Should().Be(("yes", SelectorVerdict.NoMatch));
        held.Clauses.Should().Equal("except:style-fashion/beauty");
        held.Domains.Should().Equal("style-fashion/beauty");
        held.Topics.Should().Equal("cosmetics", "skin-care");
        held.Timeframe.Should().Be("upcoming");
        var passed = diff.Items.Single(item => item.PostId == runway.Post.Id);
        (passed.Filter, passed.Selector).Should().Be(("no", SelectorVerdict.Match));
        passed.Clauses.Should().Equal("type:event", "topic:fashion-show");
        var trade = diff.Items.Single(item => item.PostId == textile.Post.Id);
        (trade.Filter, trade.Selector).Should().Be(("no", SelectorVerdict.Match));
        trade.Clauses.Should().Equal("type:event", "domain:business-and-finance/industries/apparel-industry", "topic:textile");
        diff.Items.Select(item => item.PostId).Should().NotContain(new[] { week.Post.Id, marathon.Post.Id, waiting.Post.Id, failing.Post.Id });

        var counts = PostHost.Value(await scope.Operations.DiffAsync(new SelectorDiffRequest { SubscriptionId = subscription.Id, FilterKey = FilterKey, Items = false }));
        counts.Items.Should().BeEmpty();
        (counts.Posts, counts.FilterOnly, counts.SelectorOnly).Should().Be((9, 2, 2));

        var withPending = PostHost.Value(await scope.Operations.DiffAsync(new SelectorDiffRequest
        {
            SubscriptionId = subscription.Id, FilterKey = FilterKey, IncludePending = true, Limit = 100,
        }));
        withPending.Items.Should().HaveCount(5);
        var pending = withPending.Items[^1];
        pending.PostId.Should().Be(waiting.Post.Id);
        (pending.Filter, pending.Selector).Should().Be(("yes", SelectorVerdict.Pending));
        pending.Clauses.Should().Equal("untagged");

        PostHost.Value(await scope.Operations.DiffAsync(new SelectorDiffRequest { SubscriptionId = subscription.Id, FilterKey = FilterKey, Limit = 3 }))
            .Items.Select(item => item.PostId).Should().Equal(expected.Take(3));

        host.Clock.Advance(TimeSpan.FromDays(8));
        PostHost.Value(await scope.Operations.DiffAsync(new SelectorDiffRequest { SubscriptionId = subscription.Id, FilterKey = FilterKey })).Posts
            .Should().Be(0, "the default window reaches back seven days");
        PostHost.Value(await scope.Operations.DiffAsync(new SelectorDiffRequest
        {
            SubscriptionId = subscription.Id, FilterKey = FilterKey, SinceUtc = host.Clock.GetUtcNow().AddDays(-9),
        })).Posts.Should().Be(9);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_subscription_event WHERE subscription_id=@id", ("id", subscription.Id)))
            .Should().Be(4, "the diff wrote nothing: one post.new per post the selector passes");
    }

    [Fact]
    public async Task Diff_ShouldScrubTitles_AndKeepTheLimit()
    {
        await using var host = await PostHost.StartAsync();
        await using var scope = Open(host);
        var subscription = PostHost.Value(await scope.Delivery.SubscribeAsync(Declaration(host, Sports)));
        var contact = await SaveAsync(host, scope, "contact", "Fashion fair, write info@example.com or call +998 90 123 45 67 today", "A fair.", FashionTags);
        var longTitle = await SaveAsync(host, scope, "long", "Fashion " + new string('x', 300), "A fair.", FashionTags);
        var plain = await SaveAsync(host, scope, "plain", "Fashion", "A fair.", FashionTags);

        var diff = PostHost.Value(await scope.Operations.DiffAsync(new SelectorDiffRequest { SubscriptionId = subscription.Id, FilterKey = FilterKey, Limit = 2 }));

        diff.FilterOnly.Should().Be(3);
        diff.Items.Should().HaveCount(2, "the limit caps the list, never the counts");
        var all = PostHost.Value(await scope.Operations.DiffAsync(new SelectorDiffRequest { SubscriptionId = subscription.Id, FilterKey = FilterKey }));
        var byId = all.Items.ToDictionary(item => item.PostId);
        byId[contact.Post.Id].Title.Should().Be("Fashion fair, write … or call … today");
        byId[longTitle.Post.Id].Title.Should().HaveLength(140).And.StartWith("Fashion xxx");
        byId[plain.Post.Id].Title.Should().Be("Fashion");
        diff.Items.Select(item => item.PostId).Should().Equal(all.Items.Take(2).Select(item => item.PostId));
    }

    [Fact]
    public async Task Diff_ShouldRefuseAnUnregisteredFilter_AndWhatIsNotADiff()
    {
        await using var host = await PostHost.StartAsync();
        await using var scope = Open(host);
        var subscription = PostHost.Value(await scope.Delivery.SubscribeAsync(Declaration(host, Fashion)));
        var channel = PostHost.Value(await scope.Delivery.SubscribeAsync(new SubscriptionDeclaration
        {
            TenantId = host.TenantId, ContainerId = host.ContainerId, PipelineId = host.PipelineId,
        }));
        var now = host.Clock.GetUtcNow();

        ExpectFailure(await scope.Operations.DiffAsync(new SelectorDiffRequest { SubscriptionId = subscription.Id, FilterKey = "no-such-filter-v1" }), AppErrorType.Validation);
        ExpectFailure(await scope.Operations.DiffAsync(new SelectorDiffRequest { SubscriptionId = subscription.Id, FilterKey = " " }), AppErrorType.Validation);
        ExpectFailure(await scope.Operations.DiffAsync(new SelectorDiffRequest { SubscriptionId = subscription.Id, FilterKey = FilterKey, Limit = 0 }), AppErrorType.Validation);
        ExpectFailure(await scope.Operations.DiffAsync(new SelectorDiffRequest { SubscriptionId = subscription.Id, FilterKey = FilterKey, Limit = 1001 }), AppErrorType.Validation);
        ExpectFailure(await scope.Operations.DiffAsync(new SelectorDiffRequest { SubscriptionId = subscription.Id, FilterKey = FilterKey, SinceUtc = now.AddMinutes(1) }), AppErrorType.Validation);
        ExpectFailure(await scope.Operations.DiffAsync(new SelectorDiffRequest
        {
            SubscriptionId = subscription.Id, FilterKey = FilterKey, SinceUtc = now.AddDays(-SelectorRules.MaxWindowDays - 1),
        }), AppErrorType.Validation);
        ExpectFailure(await scope.Operations.DiffAsync(new SelectorDiffRequest { SubscriptionId = channel.Id, FilterKey = FilterKey }), AppErrorType.Validation);
        ExpectFailure(await scope.Operations.DiffAsync(new SelectorDiffRequest { SubscriptionId = Guid.NewGuid(), FilterKey = FilterKey }), AppErrorType.NotFound);

        PostHost.Value(await scope.Operations.DiffAsync(new SelectorDiffRequest { SubscriptionId = subscription.Id, FilterKey = "tag:concert" }))
            .FilterKey.Should().Be("tag:concert", "a tag filter key is a filter too");
    }

    [Fact]
    public async Task Explain_ShouldNameTheVerdictClausesContextAndHeldState()
    {
        await using var host = await PostHost.StartAsync();
        await using var scope = Open(host);
        var group = PostHost.Value(await host.Registry.DeclareSourceGroupAsync(new SourceGroupDeclaration { ContainerId = host.ContainerId, Name = "Afisha" }));
        PostHost.Value(await host.Registry.UpdateSourceAsync(host.SourceId, new SourceUpdate { GroupId = group.Id }));
        var selector = Fashion with
        {
            Sources = new SelectorSources
            {
                Include = [new SelectorSourceRef { GroupId = group.Id, Context = new Dictionary<string, string> { ["defaultKind"] = "exhibition" } }],
            },
        };
        var subscription = PostHost.Value(await scope.Delivery.SubscribeAsync(Declaration(host, selector)));
        var week = await SaveAsync(host, scope, "week", "Tashkent Fashion Week", "A fashion week.", FashionTags);

        var held = PostHost.Value(await scope.Operations.ExplainAsync(subscription.Id, week.Post.Id));

        held.PostId.Should().Be(week.Post.Id);
        held.Revision.Should().Be(1);
        held.Held.Should().Be("held");
        held.Verdict.Should().Be(SelectorVerdict.Match);
        held.Clauses.Should().Equal("type:event", "domain:style-fashion");
        held.Context.Should().Equal(new Dictionary<string, string> { ["catalog"] = "ocharo-fashion", ["defaultKind"] = "exhibition" });
        held.EntityType.Should().Be("event");
        held.Domains.Should().Equal("style-fashion/high-fashion");
        held.Topics.Should().Equal("fashion-week");

        await ReviseAsync(host, scope, "week", "Tashkent Fashion Week", "Now a marathon.", SportsTags);
        var unmatched = PostHost.Value(await scope.Operations.ExplainAsync(subscription.Id, week.Post.Id));
        (unmatched.Revision, unmatched.Held, unmatched.Verdict).Should().Be((2, "unmatched", SelectorVerdict.NoMatch));
        unmatched.Clauses.Should().Equal("no-subject");
        unmatched.Domains.Should().Equal("sports");

        PostHost.Value(await scope.Delivery.SetStateAsync(subscription.Id, SubscriptionState.Suspended));
        var late = await SaveAsync(host, scope, "late", "Late Fashion Week", "A fashion week.", FashionTags);
        PostHost.Value(await scope.Delivery.SetStateAsync(subscription.Id, SubscriptionState.Active));
        var none = PostHost.Value(await scope.Operations.ExplainAsync(subscription.Id, late.Post.Id));
        (none.Held, none.Verdict).Should().Be(("none", SelectorVerdict.Match), "the log never saw the post, but its current revision passes");

        var untagged = await SaveAsync(host, scope, "untagged", "Fashion news", "Not tagged yet.");
        var pending = PostHost.Value(await scope.Operations.ExplainAsync(subscription.Id, untagged.Post.Id));
        (pending.Held, pending.Verdict).Should().Be(("none", SelectorVerdict.Pending));
        pending.Clauses.Should().Equal("untagged");
        pending.EntityType.Should().BeNull();
        pending.Domains.Should().BeEmpty();

        var other = PostHost.Value(await host.Registry.RegisterTenantAsync(new TenantRegistration { Slug = "other", Name = "Other" }));
        var foreign = PostHost.Value(await host.Registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = other.Id }));
        var foreignSource = PostHost.Value(await host.Registry.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = foreign.Id, Family = SourceFamily.Site, Handle = "foreign.uz", Coverage = SourceCoverage.Whole,
            Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch, Authority = 50, CheckPolicy = "web-direct",
        })).Id;
        var foreignPost = PostHost.Value(await scope.Posts.SaveCaptureAsync(foreign.Id, foreignSource, null, PostStoreTests.Capture(host, "foreign", "A fashion week.", "foreign.uz"),
            Tagged("Foreign fashion week", "A fashion week.", FashionTags)));
        var channel = PostHost.Value(await scope.Delivery.SubscribeAsync(new SubscriptionDeclaration
        {
            TenantId = host.TenantId, ContainerId = host.ContainerId, PipelineId = host.PipelineId,
        }));
        ExpectFailure(await scope.Operations.ExplainAsync(subscription.Id, foreignPost.Post.Id), AppErrorType.NotFound);
        ExpectFailure(await scope.Operations.ExplainAsync(subscription.Id, Guid.NewGuid()), AppErrorType.NotFound);
        ExpectFailure(await scope.Operations.ExplainAsync(Guid.NewGuid(), week.Post.Id), AppErrorType.NotFound);
        ExpectFailure(await scope.Operations.ExplainAsync(channel.Id, week.Post.Id), AppErrorType.Validation);
    }

    [Fact]
    public async Task Adopt_ShouldUnmatchRejectedPosts_AndSkipHeldOnes()
    {
        await using var host = await PostHost.StartAsync();
        await using var scope = Open(host);
        var seed = await SeedAdoptionAsync(host, scope);

        var adopted = PostHost.Value(await scope.Operations.AdoptAsync(new SelectorAdoptRequest { IntoSubscriptionId = seed.Into.Id, FromSubscriptionIds = [seed.Old.Id] }));

        (adopted.Considered, adopted.AlreadyHeld, adopted.Unmatched, adopted.New, adopted.Pending, adopted.DryRun).Should().Be((5, 1, 2, 1, 1, false));
        var page = await PullUntilAsync(scope.Delivery, seed.Into.Id, page => page.Data.Count >= 4);
        page.Data.Select(item => item.PostId).Should().Equal(seed.D, seed.A, seed.B, seed.E);
        page.Data.Select(item => item.Type).Should().Equal(DeliveryEventTypes.PostNew, DeliveryEventTypes.PostNew, DeliveryEventTypes.PostUnmatched, DeliveryEventTypes.PostUnmatched);
        page.Data.Select(item => item.Reason).Should().Equal("ingest", "backfill", "resync", "resync");
        page.Data.Should().OnlyContain(item => item.Match!.SelectorVersion == 1);
        page.Data.Select(item => item.Match!.Verdict).Should().Equal(SelectorVerdict.Match, SelectorVerdict.Match, SelectorVerdict.NoMatch, SelectorVerdict.NoMatch);
        page.Data[1].Match!.Clauses.Should().Equal("type:event", "domain:style-fashion");
        page.Data[2].Match!.Clauses.Should().Equal("no-subject");
        page.Data[1].ItemId.Should().Be(Item(seed.A, 1, DeliveryEventTypes.PostNew, ":s1"));
        page.Data[2].ItemId.Should().Be(Item(seed.B, 1, DeliveryEventTypes.PostUnmatched, ":s1"));
        page.Data[3].ItemId.Should().Be(Item(seed.E, 2, DeliveryEventTypes.PostUnmatched, ":s1"));
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_subscription_event WHERE subscription_id=@id AND post_id=@post", ("id", seed.Into.Id), ("post", seed.C)))
            .Should().Be(0, "a post the selector cannot judge yet gets nothing");
    }

    [Fact]
    public async Task Adopt_ShouldLeaveAlonePostsAFromSubscriptionAlreadyDropped()
    {
        await using var host = await PostHost.StartAsync();
        await using var scope = Open(host);
        var seed = await SeedAdoptionAsync(host, scope);

        var adopted = PostHost.Value(await scope.Operations.AdoptAsync(new SelectorAdoptRequest { IntoSubscriptionId = seed.Into.Id, FromSubscriptionIds = [seed.Filtered.Id] }));

        (adopted.Considered, adopted.AlreadyHeld, adopted.Unmatched, adopted.New, adopted.Pending).Should().Be((3, 1, 0, 1, 1),
            "the filter never delivered the marathon and unmatched the revised expo, so only the fashion week, the news and the gala are considered");
        var page = await PullUntilAsync(scope.Delivery, seed.Into.Id, page => page.Data.Count >= 2);
        page.Data.Select(item => item.PostId).Should().Equal(seed.D, seed.A);
    }

    [Fact]
    public async Task Adopt_ShouldWriteNothing_OnADryRun_OrASecondRun()
    {
        await using var host = await PostHost.StartAsync();
        await using var scope = Open(host);
        var seed = await SeedAdoptionAsync(host, scope);
        var request = new SelectorAdoptRequest { IntoSubscriptionId = seed.Into.Id, FromSubscriptionIds = [seed.Old.Id, seed.Filtered.Id] };

        var dry = PostHost.Value(await scope.Operations.AdoptAsync(request with { DryRun = true }));
        (dry.Considered, dry.AlreadyHeld, dry.Unmatched, dry.New, dry.Pending, dry.DryRun).Should().Be((5, 1, 2, 1, 1, true));
        (await EventCountAsync(host, seed.Into.Id)).Should().Be(1, "a dry run rolls back");

        var real = PostHost.Value(await scope.Operations.AdoptAsync(request));
        (real.Considered, real.AlreadyHeld, real.Unmatched, real.New, real.Pending, real.DryRun).Should().Be((5, 1, 2, 1, 1, false));
        (await EventCountAsync(host, seed.Into.Id)).Should().Be(4);

        var again = PostHost.Value(await scope.Operations.AdoptAsync(request));
        (again.Considered, again.AlreadyHeld, again.Unmatched, again.New, again.Pending).Should().Be((5, 4, 0, 0, 1));
        (await EventCountAsync(host, seed.Into.Id)).Should().Be(4, "a second run writes nothing");
    }

    [Fact]
    public async Task Adopt_ShouldSkipPostsThatAreNoLongerLive()
    {
        await using var host = await PostHost.StartAsync();
        await using var scope = Open(host);
        var seed = await SeedAdoptionAsync(host, scope);
        PostHost.Value(await scope.Posts.ObserveLinkAsync(host.ContainerId, host.SourceId,
            new LinkObservation { ExternalKey = "b", Signal = LinkSignal.StrongNegative, Reason = "direct_410", HttpStatus = 410 }));
        (await host.ScalarAsync<string>("SELECT state FROM harvest_posts WHERE id=@id", ("id", seed.B))).Should().Be("gone", "the old subscription's post was deleted");

        var adopted = PostHost.Value(await scope.Operations.AdoptAsync(new SelectorAdoptRequest { IntoSubscriptionId = seed.Into.Id, FromSubscriptionIds = [seed.Old.Id] }));

        (adopted.Considered, adopted.AlreadyHeld, adopted.Unmatched, adopted.New, adopted.Pending).Should().Be((4, 1, 1, 1, 1),
            "the deleted marathon is not considered: the old subscription already told Events it is gone");
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_subscription_event WHERE subscription_id=@id AND post_id=@post", ("id", seed.Into.Id), ("post", seed.B)))
            .Should().Be(0, "the selector never learns a deleted post");
        var page = await PullUntilAsync(scope.Delivery, seed.Into.Id, page => page.Data.Count >= 3);
        page.Data.Select(item => item.PostId).Should().Equal(seed.D, seed.A, seed.E);
        (await EventCountAsync(host, seed.Into.Id)).Should().Be(3);
    }

    [Fact]
    public async Task Adopt_ShouldRefuseWhatIsNotAnAdoption_AndWriteNothing()
    {
        await using var host = await PostHost.StartAsync();
        await using var scope = Open(host);
        var seed = await SeedAdoptionAsync(host, scope);
        var other = PostHost.Value(await host.Registry.RegisterTenantAsync(new TenantRegistration { Slug = "other", Name = "Other" }));
        var foreign = PostHost.Value(await host.Registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = other.Id }));
        var foreignSelector = PostHost.Value(await scope.Delivery.SubscribeAsync(new SubscriptionDeclaration
        {
            TenantId = other.Id, ContainerId = foreign.Id, Name = "foreign-events", Selector = Fashion,
        }));
        var suspended = PostHost.Value(await scope.Delivery.SubscribeAsync(Declaration(host, Sports) with { Name = "suspended-events", Backfill = BackfillMode.None }));
        PostHost.Value(await scope.Delivery.SetStateAsync(suspended.Id, SubscriptionState.Suspended));

        ExpectFailure(await Adopt(scope, seed.Old.Id, seed.Old.Id), AppErrorType.Validation);
        ExpectFailure(await Adopt(scope, seed.Into.Id, seed.Into.Id, seed.Old.Id), AppErrorType.Validation);
        ExpectFailure(await Adopt(scope, seed.Into.Id), AppErrorType.Validation);
        ExpectFailure(await Adopt(scope, seed.Old.Id, seed.Filtered.Id), AppErrorType.Validation);
        ExpectFailure(await Adopt(scope, suspended.Id, seed.Old.Id), AppErrorType.Conflict);
        ExpectFailure(await Adopt(scope, seed.Into.Id, foreignSelector.Id), AppErrorType.Validation);
        ExpectFailure(await Adopt(scope, seed.Into.Id, Guid.NewGuid()), AppErrorType.NotFound);
        ExpectFailure(await Adopt(scope, Guid.NewGuid(), seed.Old.Id), AppErrorType.NotFound);

        (await EventCountAsync(host, seed.Into.Id)).Should().Be(1);
    }

    /// <summary>Saves the five posts an adoption cares about. <c>A</c> a fashion week the selector passes, <c>B</c> a marathon it
    /// refuses, <c>C</c> an untagged post it cannot judge yet, <c>E</c> an expo revised into a marathon; the unfiltered
    /// channel <c>Old</c> holds all four, the filtered one every post whose title has "fashion" until its revision. The
    /// selector subscribes (without a backfill) before <c>D</c>, a second fashion week that it holds already.</summary>
    private static async Task<Adoption> SeedAdoptionAsync(PostHost host, OperationsScope scope)
    {
        var old = PostHost.Value(await scope.Delivery.SubscribeAsync(new SubscriptionDeclaration
        {
            TenantId = host.TenantId, ContainerId = host.ContainerId, PipelineId = host.PipelineId,
        }));
        var filtered = PostHost.Value(await scope.Delivery.SubscribeAsync(new SubscriptionDeclaration
        {
            TenantId = host.TenantId, ContainerId = host.ContainerId, PipelineId = host.PipelineId, FilterKey = FilterKey,
        }));
        var a = await SaveAsync(host, scope, "a", "Fashion week", "A fashion week.", FashionTags);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var b = await SaveAsync(host, scope, "b", "A marathon", "A marathon.", SportsTags);
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var c = await SaveAsync(host, scope, "c", "Fashion news", "Not tagged yet.");
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var e = await SaveAsync(host, scope, "e", "Fashion expo", "A fashion expo.", FashionTags);
        await ReviseAsync(host, scope, "e", "A marathon of the expo", "Now a marathon.", SportsTags);
        var into = PostHost.Value(await scope.Delivery.SubscribeAsync(Declaration(host, Fashion) with { Backfill = BackfillMode.None }));
        host.Clock.Advance(TimeSpan.FromMinutes(1));
        var d = await SaveAsync(host, scope, "d", "Fashion gala", "A fashion gala.", FashionTags);
        return new Adoption(old, filtered, into, a.Post.Id, b.Post.Id, c.Post.Id, d.Post.Id, e.Post.Id);
    }

    private sealed record Adoption(SubscriptionModel Old, SubscriptionModel Filtered, SubscriptionModel Into, Guid A, Guid B, Guid C, Guid D, Guid E);

    private static Task<Result<SelectorAdoptModel>> Adopt(OperationsScope scope, Guid into, params Guid[] from) =>
        scope.Operations.AdoptAsync(new SelectorAdoptRequest { IntoSubscriptionId = into, FromSubscriptionIds = from });

    private static Task<long> EventCountAsync(PostHost host, Guid subscriptionId) =>
        host.ScalarAsync<long>("SELECT count(*) FROM harvest_subscription_event WHERE subscription_id=@id", ("id", subscriptionId));

    private static SubscriptionDeclaration Declaration(PostHost host, SubscriptionSelector selector) => new()
    {
        TenantId = host.TenantId, ContainerId = host.ContainerId, Name = "ocharo-events", Selector = selector,
        Context = new Dictionary<string, string> { ["catalog"] = "ocharo-fashion" },
    };

    private static PostEnvelope Tagged(string title, string text, string[] tags) => PostStoreTests.Envelope(title, text) with
    {
        Tags = [.. tags.Select(tag => new PostTag { Tag = tag, Tier = ExtractionTier.SmallModel })],
    };

    /// <summary>Saves a capture whose envelope carries <paramref name="tags"/> as the Layer 1 answer; none leaves it untagged.</summary>
    private static async Task<CaptureSaveResult> SaveAsync(PostHost host, OperationsScope scope, string key, string title, string text, string[]? tags = null,
        Guid? sourceId = null, string domain = "mstar.uz") =>
        PostHost.Value(await scope.Posts.SaveCaptureAsync(host.ContainerId, sourceId ?? host.SourceId, null, PostStoreTests.Capture(host, key, text, domain),
            Tagged(title, text, tags ?? [])));

    private static async Task<CaptureSaveResult> ReviseAsync(PostHost host, OperationsScope scope, string key, string title, string text, string[] tags)
    {
        host.Clock.Advance(TimeSpan.FromHours(1));
        return await SaveAsync(host, scope, key, title, text, tags);
    }

    private static Guid[] InPostOrder(CaptureSaveResult first, CaptureSaveResult second) =>
        [.. new[] { first.Post.Id, second.Post.Id }.OrderBy(id => id.ToString("N"), StringComparer.Ordinal)];

    private static string Item(Guid postId, int revision, string type, string suffix = "") =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{postId:N}:{revision}:{type}{suffix}"))).ToLowerInvariant();

    private static void ExpectFailure<T>(Result<T> result, AppErrorType type) where T : notnull =>
        result.Should().BeOfType<Result<T>.Failure>().Which.Error.Type.Should().Be(type);

    private static async Task<CursorPage<DeliveryEvent>> PullUntilAsync(IDeliveryBroker delivery, Guid subscriptionId, Func<CursorPage<DeliveryEvent>, bool> ready)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            var page = PostHost.Value(await delivery.PullAsync(subscriptionId));
            if (ready(page) || elapsed.Elapsed > TimeSpan.FromSeconds(5)) return page;
            await Task.Delay(50);
        }
    }

    /// <summary>Opens the module again over the host's database with the operations broker and the test filter registered.</summary>
    private static OperationsScope Open(PostHost host)
    {
        var services = new ServiceCollection().AddLogging().AddSingleton<TimeProvider>(host.Clock).AddSingleton<IDeliveryPostFilter>(new FashionFilter());
        services.AddHarvest().Configure(options => options.ConnectionString = host.ConnectionString);
        services.AddHarvestPosts().AddHarvestDelivery().AddHarvestSubscriptionOperations();
        var provider = services.BuildServiceProvider(validateScopes: true);
        return new OperationsScope(provider, provider.CreateAsyncScope());
    }

    private sealed class OperationsScope(ServiceProvider provider, AsyncServiceScope scope) : IAsyncDisposable
    {
        internal IPostBroker Posts => scope.ServiceProvider.GetRequiredService<IPostBroker>();
        internal IDeliveryBroker Delivery => scope.ServiceProvider.GetRequiredService<IDeliveryBroker>();
        internal ISubscriptionOperationsBroker Operations => scope.ServiceProvider.GetRequiredService<ISubscriptionOperationsBroker>();
        public async ValueTask DisposeAsync() { await scope.DisposeAsync(); await provider.DisposeAsync(); }
    }

    /// <summary>Stands in for Ocharo's relevance filter: passes a post whose title or text says fashion or accessory, and fails on
    /// a title that says explode.</summary>
    private sealed class FashionFilter : IDeliveryPostFilter
    {
        public string Key => FilterKey;

        public bool Matches(PostEnvelope envelope)
        {
            if (envelope.Title?.Contains("explode", StringComparison.OrdinalIgnoreCase) is true)
                throw new InvalidOperationException("The filter failed.");
            return new[] { envelope.Title, envelope.Text }.Any(text => text is not null
                && (text.Contains("fashion", StringComparison.OrdinalIgnoreCase) || text.Contains("аксессуар", StringComparison.OrdinalIgnoreCase)));
        }
    }
}
