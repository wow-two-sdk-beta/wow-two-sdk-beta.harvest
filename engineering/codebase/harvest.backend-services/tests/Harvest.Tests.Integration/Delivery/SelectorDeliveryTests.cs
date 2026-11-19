using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using Harvest.Tests.Integration.Posts;
using Harvest.Tests.Integration.Tagging;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Ai.Testing.Providers;
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

/// <summary>Selector subscriptions over PostgreSQL: subscribe and backfill, the verdict each write carries, the change
/// window on the same cursor, the state machine, and the rendered payload. Posts carry their Layer 1 tags in the capture's
/// envelope, which a post without a stored answer keeps as resolved; <c>TagsHost</c> runs the real answer path.</summary>
[Collection("Post delivery transactions")]
public sealed class SelectorDeliveryTests
{
    private static readonly string[] FashionTags = ["type:event", "domain:style-fashion/high-fashion", "domain:style-fashion", "topic:fashion-week"];
    private static readonly string[] SportsTags = ["type:event", "domain:sports", "topic:marathon"];

    private static readonly SubscriptionSelector Fashion = new() { EntityTypes = ["event"], Domains = [new SelectorDomain { Key = "style-fashion" }] };
    private static readonly SubscriptionSelector Sports = new() { EntityTypes = ["event"], Domains = [new SelectorDomain { Key = "sports" }] };

    [Fact]
    public async Task Subscribe_ShouldBackfillTaggedMatches_AndSkipPendingPosts()
    {
        await using var host = await PostHost.StartAsync();
        var fashion = await SaveAsync(host, "fashion", "A fashion week.", FashionTags);
        await SaveAsync(host, "sports", "A marathon.", SportsTags);
        await SaveAsync(host, "untagged", "Not tagged yet.");

        var subscription = PostHost.Value(await host.Delivery.SubscribeAsync(Declaration(host, Fashion)));

        subscription.Kind.Should().Be(SubscriptionKind.Selector);
        subscription.PipelineId.Should().BeNull();
        subscription.Name.Should().Be("ocharo-events");
        subscription.SelectorVersion.Should().Be(1);
        subscription.Selector.Should().BeEquivalentTo(Fashion);
        subscription.State.Should().Be(SubscriptionState.Active);
        var page = await host.PullUntilAsync(subscription.Id, page => page.Data.Count >= 1);
        var item = page.Data.Should().ContainSingle().Subject;
        item.Type.Should().Be(DeliveryEventTypes.PostNew);
        item.Reason.Should().Be("backfill");
        item.PostId.Should().Be(fashion.Post.Id);
        item.PipelineId.Should().BeNull();
        item.Context["catalog"].Should().Be("ocharo-fashion");
        item.Match.Should().NotBeNull();
        item.Match!.SelectorVersion.Should().Be(1);
        item.Match.Verdict.Should().Be(SelectorVerdict.Match);
        item.Match.Clauses.Should().Equal("type:event", "domain:style-fashion");
        item.ItemId.Should().Be(Item(fashion.Post.Id, 1, DeliveryEventTypes.PostNew));
        (await host.ScalarAsync<string>("""
            SELECT (counts->>'considered') || ',' || (counts->>'newMatches') || ',' || (counts->>'pending') FROM harvest_subscription_selectors
            WHERE subscription_id=@id AND version=1
            """, ("id", subscription.Id))).Should().Be("3,1,1");
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_subscription_selectors WHERE subscription_id=@id", ("id", subscription.Id))).Should().Be(1);
        PostHost.Value(await host.Delivery.GetSubscriptionAsync(subscription.Id)).Selector.Should().BeEquivalentTo(Fashion);
        PostHost.Value(await host.Delivery.ListSubscriptionsAsync(host.TenantId)).Should().ContainSingle(item => item.Id == subscription.Id);
    }

    [Fact]
    public async Task Subscribe_ShouldBackfillOnlyPostsFirstSeenSinceTheBound_WhenTheBackfillIsSince()
    {
        await using var host = await PostHost.StartAsync();
        await SaveAsync(host, "before", "A fashion week before the bound.", FashionTags);
        host.Clock.Advance(TimeSpan.FromDays(2));
        var bound = host.Clock.GetUtcNow();
        var after = await SaveAsync(host, "after", "A fashion week after the bound.", FashionTags);

        var subscription = PostHost.Value(await host.Delivery.SubscribeAsync(Declaration(host, Fashion) with
        {
            Backfill = BackfillMode.Since, BackfillSinceUtc = bound,
        }));

        var page = await host.PullUntilAsync(subscription.Id, page => page.Data.Count >= 1);
        page.Data.Should().ContainSingle().Which.PostId.Should().Be(after.Post.Id);
        (await host.ScalarAsync<DateTime>("SELECT window_since FROM harvest_subscription_selectors WHERE subscription_id=@id", ("id", subscription.Id)))
            .Should().Be(bound.UtcDateTime);
    }

    [Fact]
    public async Task Subscribe_ShouldRefuseWhatASelectorSubscriptionCannotBe_AndWriteNothing()
    {
        await using var host = await PostHost.StartAsync();
        PostHost.Value(await host.Delivery.SubscribeAsync(Declaration(host, Fashion)));
        var other = PostHost.Value(await host.Registry.RegisterTenantAsync(new TenantRegistration { Slug = "other", Name = "Other" }));
        var foreign = PostHost.Value(await host.Registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = other.Id }));

        await ExpectFailureAsync(host, Declaration(host, Fashion), AppErrorType.Conflict);
        await ExpectFailureAsync(host, Declaration(host, Fashion) with { Name = null }, AppErrorType.Validation);
        await ExpectFailureAsync(host, Declaration(host, Fashion) with { Name = "Not A Slug" }, AppErrorType.Validation);
        await ExpectFailureAsync(host, Declaration(host, Fashion) with { Name = "with-pipeline", PipelineId = host.PipelineId }, AppErrorType.Validation);
        await ExpectFailureAsync(host, Declaration(host, Fashion) with { Name = "with-filter", FilterKey = "tag:concert" }, AppErrorType.Validation);
        await ExpectFailureAsync(host, Declaration(host, Fashion) with { Name = "bad-since", Backfill = BackfillMode.Since }, AppErrorType.Validation);
        await ExpectFailureAsync(host, Declaration(host, Fashion with { EntityTypes = ["movie"] }) with { Name = "bad-type" }, AppErrorType.Validation);
        await ExpectFailureAsync(host, Declaration(host, Fashion) with { Name = "foreign-container", ContainerId = foreign.Id }, AppErrorType.NotFound);
        await ExpectFailureAsync(host, Declaration(host, Fashion) with { Name = "foreign-tenant", TenantId = Guid.NewGuid() }, AppErrorType.NotFound);
        await ExpectFailureAsync(host, new SubscriptionDeclaration
        {
            TenantId = host.TenantId, ContainerId = host.ContainerId, PipelineId = host.PipelineId, Name = "channel-with-a-name",
        }, AppErrorType.Validation);
        await ExpectFailureAsync(host, new SubscriptionDeclaration { TenantId = host.TenantId, ContainerId = host.ContainerId }, AppErrorType.Validation);

        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_subscriptions")).Should().Be(1);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_subscription_selectors")).Should().Be(1);
    }

    [Fact]
    public async Task Write_ShouldSendPostNew_WhenTheTagsAnswerArrives()
    {
        await using var host = await TagsHost.StartAsync();
        var subscription = PostHost.Value(await host.Delivery.SubscribeAsync(new SubscriptionDeclaration
        {
            TenantId = host.TenantId, ContainerId = host.ContainerId, Name = "ocharo-events", Selector = Fashion,
        }));

        var saved = await host.SaveAsync("fw", "Tashkent Fashion Week", "Moda haftaligi dekabrda bo'ladi.");

        PostHost.Value(await host.Delivery.PullAsync(subscription.Id)).Data.Should().BeEmpty("the post waits for its tags: pending sends nothing");
        host.Caller.Enqueue(ScriptedAiModelCaller.Answer(TagsHost.Answer(domains: [new { node = "style-fashion/high-fashion", primary = true }])));
        (await host.Tags.ProcessDueAsync()).Should().Be(1);
        var page = await PullUntilAsync(host.Delivery, subscription.Id, page => page.Data.Count >= 1);
        var item = page.Data.Should().ContainSingle().Subject;
        item.Type.Should().Be(DeliveryEventTypes.PostNew);
        item.PostId.Should().Be(saved.Post.Id);
        item.Revision.Should().Be(2);
        item.Reason.Should().Be("ingest");
        item.Match!.Verdict.Should().Be(SelectorVerdict.Match);
        item.Match.Clauses.Should().Equal("type:event", "domain:style-fashion");
        item.ItemId.Should().Be(Item(saved.Post.Id, 2, DeliveryEventTypes.PostNew));
    }

    [Fact]
    public async Task Write_ShouldUnmatchOnce_WhenANewRevisionLeavesTheSelector()
    {
        await using var host = await PostHost.StartAsync();
        var subscription = PostHost.Value(await host.Delivery.SubscribeAsync(Declaration(host, Fashion)));
        var saved = await SaveAsync(host, "week", "A fashion week.", FashionTags);
        await ReviseAsync(host, "week", "Now a marathon.", SportsTags);
        await ReviseAsync(host, "week", "A longer marathon.", SportsTags);
        await ReviseAsync(host, "week", "A fashion week again.", FashionTags);

        var page = await host.PullUntilAsync(subscription.Id, page => page.Data.Count >= 3);

        page.Data.Select(item => item.Type).Should().Equal(DeliveryEventTypes.PostNew, DeliveryEventTypes.PostUnmatched, DeliveryEventTypes.PostChanged);
        page.Data.Select(item => item.Revision).Should().Equal(1, 2, 4);
        page.Data.Select(item => item.Match!.Verdict).Should().Equal(SelectorVerdict.Match, SelectorVerdict.NoMatch, SelectorVerdict.Match);
        page.Data[1].Match!.Clauses.Should().Equal("no-subject");
        page.Data.Should().OnlyContain(item => item.PostId == saved.Post.Id && item.Reason == "ingest" && item.Match!.SelectorVersion == 1);
        page.Data.Select(item => item.ItemId).Distinct().Should().HaveCount(3);
    }

    [Fact]
    public async Task Write_ShouldWithholdLifecycleEvents_OfAPostTheSelectorNeverHeld()
    {
        await using var host = await PostHost.StartAsync();
        var subscription = PostHost.Value(await host.Delivery.SubscribeAsync(Declaration(host, Fashion)));
        var held = await SaveAsync(host, "held", "A fashion week.", FashionTags);
        await SaveAsync(host, "never", "A marathon.", SportsTags);

        foreach (var key in new[] { "held", "never" })
            PostHost.Value(await host.Posts.ObserveLinkAsync(host.ContainerId, host.SourceId,
                new LinkObservation { ExternalKey = key, Signal = LinkSignal.StrongNegative, Reason = "direct_410", HttpStatus = 410 }));

        var page = await host.PullUntilAsync(subscription.Id, page => page.Data.Count >= 2);
        page.Data.Select(item => item.Type).Should().Equal(DeliveryEventTypes.PostNew, DeliveryEventTypes.LinkState);
        page.Data.Should().OnlyContain(item => item.PostId == held.Post.Id);
        page.Data[1].Match!.Verdict.Should().Be(SelectorVerdict.Match);
    }

    [Fact]
    public async Task Write_ShouldJudgeSourcesPerLink_AndLetExcludeWin()
    {
        await using var host = await PostHost.StartAsync();
        var second = await host.SourceAsync("second.uz");
        var third = await host.SourceAsync("third.uz");
        var group = PostHost.Value(await host.Registry.DeclareSourceGroupAsync(new SourceGroupDeclaration { ContainerId = host.ContainerId, Name = "Publishers" }));
        foreach (var source in new[] { host.SourceId, second })
            PostHost.Value(await host.Registry.UpdateSourceAsync(source, new SourceUpdate { GroupId = group.Id }));
        var selector = Fashion with
        {
            Sources = new SelectorSources
            {
                Include = [new SelectorSourceRef { GroupId = group.Id }],
                Exclude = [new SelectorSourceRef { SourceId = second }],
            },
        };
        var subscription = PostHost.Value(await host.Delivery.SubscribeAsync(Declaration(host, selector)));

        var included = await SaveAsync(host, "included", "A fashion week.", FashionTags);
        await SaveAsync(host, "excluded", "A fashion week in an excluded source.", FashionTags, second, "second.uz");
        await SaveAsync(host, "outside", "A fashion week outside the group.", FashionTags, third, "third.uz");

        var page = await host.PullUntilAsync(subscription.Id, page => page.Data.Count >= 1);
        page.Data.Should().ContainSingle().Which.PostId.Should().Be(included.Post.Id);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_subscription_event WHERE subscription_id=@id", ("id", subscription.Id))).Should().Be(1);
    }

    [Fact]
    public async Task SetSelector_ShouldBackfillAndResync_OnTheSameCursor()
    {
        await using var host = await PostHost.StartAsync();
        var leaving = await SaveAsync(host, "leaving", "A fashion week.", FashionTags);
        host.Clock.Advance(TimeSpan.FromMinutes(5));
        var joining = await SaveAsync(host, "joining", "A marathon.", SportsTags);
        host.Clock.Advance(TimeSpan.FromMinutes(5));
        await SaveAsync(host, "waiting", "Not tagged yet.");
        var subscription = PostHost.Value(await host.Delivery.SubscribeAsync(Declaration(host, Fashion)));
        var first = await host.PullUntilAsync(subscription.Id, page => page.Data.Count >= 1);
        first.Data.Should().ContainSingle().Which.PostId.Should().Be(leaving.Post.Id);

        var change = PostHost.Value(await host.Delivery.SetSelectorAsync(subscription.Id, new SelectorChange { Selector = Sports, ChangedBy = "operator" }));

        change.Changed.Should().BeTrue();
        change.Subscription.SelectorVersion.Should().Be(2);
        change.Subscription.Selector.Should().BeEquivalentTo(Sports);
        change.WindowSinceUtc.Should().Be(host.Clock.GetUtcNow().AddDays(-SelectorRules.DefaultWindowDays));
        (change.Considered, change.NewMatches, change.Unmatched, change.Rematched, change.Pending).Should().Be((3, 1, 1, 0, 1));
        var page = await host.PullUntilAsync(subscription.Id, page => page.Data.Count >= 2, first.Next);
        page.Data.Select(item => item.Type).Should().Equal(DeliveryEventTypes.PostUnmatched, DeliveryEventTypes.PostNew);
        page.Data.Select(item => item.Reason).Should().Equal("resync", "backfill");
        page.Data.Select(item => item.PostId).Should().Equal(leaving.Post.Id, joining.Post.Id);
        page.Data.Should().OnlyContain(item => item.Match!.SelectorVersion == 2);
        page.Data[0].Match!.Verdict.Should().Be(SelectorVerdict.NoMatch);
        page.Data[0].Match!.Clauses.Should().Equal("no-subject");
        page.Data[1].Match!.Clauses.Should().Equal("type:event", "domain:sports");
        page.Data[0].ItemId.Should().Be(Item(leaving.Post.Id, 1, DeliveryEventTypes.PostUnmatched, ":s2"));
        page.Data[1].ItemId.Should().Be(Item(joining.Post.Id, 1, DeliveryEventTypes.PostNew, ":s2"));
        (await host.ScalarAsync<string>("""
            SELECT changed_by || '|' || (counts->>'considered') || ',' || (counts->>'newMatches') || ',' || (counts->>'unmatched') || ',' || (counts->>'rematched')
                || ',' || (counts->>'pending') FROM harvest_subscription_selectors WHERE subscription_id=@id AND version=2
            """, ("id", subscription.Id))).Should().Be("operator|3,1,1,0,1");
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_subscription_selectors WHERE subscription_id=@id", ("id", subscription.Id))).Should().Be(2);
    }

    [Fact]
    public async Task SetSelector_ShouldRematchAtTheSameRevision_WithADistinctItemId()
    {
        await using var host = await PostHost.StartAsync();
        var saved = await SaveAsync(host, "week", "A fashion week.", FashionTags);
        var subscription = PostHost.Value(await host.Delivery.SubscribeAsync(Declaration(host, Fashion)));

        PostHost.Value(await host.Delivery.SetSelectorAsync(subscription.Id, new SelectorChange { Selector = Sports }));
        var back = PostHost.Value(await host.Delivery.SetSelectorAsync(subscription.Id, new SelectorChange { Selector = Fashion }));

        (back.Subscription.SelectorVersion, back.Rematched, back.NewMatches, back.Unmatched).Should().Be((3, 1, 0, 0));
        var page = await host.PullUntilAsync(subscription.Id, page => page.Data.Count >= 3);
        page.Data.Select(item => item.Type).Should().Equal(DeliveryEventTypes.PostNew, DeliveryEventTypes.PostUnmatched, DeliveryEventTypes.PostChanged);
        page.Data.Select(item => item.Revision).Should().Equal(1, 1, 1);
        page.Data.Select(item => item.Reason).Should().Equal("backfill", "resync", "resync");
        page.Data.Select(item => item.Match!.SelectorVersion).Should().Equal(1, 2, 3);
        page.Data.Select(item => item.ItemId).Should().Equal(Item(saved.Post.Id, 1, DeliveryEventTypes.PostNew),
            Item(saved.Post.Id, 1, DeliveryEventTypes.PostUnmatched, ":s2"), Item(saved.Post.Id, 1, DeliveryEventTypes.PostChanged, ":s3"));
    }

    [Fact]
    public async Task SetSelector_ShouldReachBackOnlyAsFarAsTheWindow_ForPostsTheLogNeverHeld()
    {
        await using var host = await PostHost.StartAsync();
        var subscription = PostHost.Value(await host.Delivery.SubscribeAsync(Declaration(host, Fashion)));
        await SaveAsync(host, "old", "An old marathon.", SportsTags);
        host.Clock.Advance(TimeSpan.FromDays(10));
        var recent = await SaveAsync(host, "recent", "A recent marathon.", SportsTags);
        var window = host.Clock.GetUtcNow().AddDays(-1);

        var change = PostHost.Value(await host.Delivery.SetSelectorAsync(subscription.Id, new SelectorChange { Selector = Sports, WindowSinceUtc = window }));

        change.WindowSinceUtc.Should().Be(window);
        (change.Considered, change.NewMatches).Should().Be((1, 1));
        var page = await host.PullUntilAsync(subscription.Id, page => page.Data.Count >= 1);
        page.Data.Should().ContainSingle().Which.PostId.Should().Be(recent.Post.Id);
    }

    [Fact]
    public async Task SetSelector_ShouldWriteNothing_WhenUnchanged()
    {
        await using var host = await PostHost.StartAsync();
        await SaveAsync(host, "week", "A fashion week.", FashionTags);
        var subscription = PostHost.Value(await host.Delivery.SubscribeAsync(Declaration(host, Fashion)));

        var same = PostHost.Value(await host.Delivery.SetSelectorAsync(subscription.Id, new SelectorChange
        {
            Selector = new SubscriptionSelector { EntityTypes = [" Event "], Domains = [new SelectorDomain { Key = "STYLE-FASHION" }] },
        }));

        same.Changed.Should().BeFalse();
        same.WindowSinceUtc.Should().BeNull();
        (same.Considered, same.NewMatches, same.Unmatched, same.Rematched, same.Pending).Should().Be((0, 0, 0, 0, 0));
        same.Subscription.SelectorVersion.Should().Be(1);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_subscription_selectors WHERE subscription_id=@id", ("id", subscription.Id))).Should().Be(1);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_subscription_event WHERE subscription_id=@id", ("id", subscription.Id))).Should().Be(1);
    }

    [Fact]
    public async Task SetSelector_ShouldRefuseAWindowOverAYear_UnknownTaxonomyKeys_AndForeignRefs()
    {
        await using var host = await PostHost.StartAsync();
        var subscription = PostHost.Value(await host.Delivery.SubscribeAsync(Declaration(host, Fashion)));
        var other = PostHost.Value(await host.Registry.RegisterTenantAsync(new TenantRegistration { Slug = "other", Name = "Other" }));
        var foreign = PostHost.Value(await host.Registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = other.Id }));
        var foreignSource = PostHost.Value(await host.Registry.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = foreign.Id, Family = SourceFamily.Site, Handle = "foreign.uz", Coverage = SourceCoverage.Whole,
            Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch, Authority = 50, CheckPolicy = "web-direct",
        })).Id;
        var now = host.Clock.GetUtcNow();

        await ExpectChangeFailureAsync(host, subscription.Id, new SelectorChange { Selector = Sports, WindowSinceUtc = now.AddDays(-SelectorRules.MaxWindowDays - 1) }, AppErrorType.Validation);
        await ExpectChangeFailureAsync(host, subscription.Id, new SelectorChange { Selector = Sports, WindowSinceUtc = now.AddMinutes(1) }, AppErrorType.Validation);
        await ExpectChangeFailureAsync(host, subscription.Id, new SelectorChange { Selector = Sports with { Domains = [new SelectorDomain { Key = "no-such-domain" }] } }, AppErrorType.Validation);
        await ExpectChangeFailureAsync(host, subscription.Id, new SelectorChange
        {
            Selector = Sports with { Domains = [new SelectorDomain { Key = "sports", Except = ["sports/no-such-sport"] }] },
        }, AppErrorType.Validation);
        await ExpectChangeFailureAsync(host, subscription.Id, new SelectorChange { Selector = Sports with { ExcludeDomains = ["no-such-domain"] } }, AppErrorType.Validation);
        await ExpectChangeFailureAsync(host, subscription.Id, new SelectorChange
        {
            Selector = Sports with { Sources = new SelectorSources { Include = [new SelectorSourceRef { GroupId = Guid.NewGuid() }] } },
        }, AppErrorType.Validation);
        await ExpectChangeFailureAsync(host, subscription.Id, new SelectorChange
        {
            Selector = Sports with { Sources = new SelectorSources { Exclude = [new SelectorSourceRef { SourceId = foreignSource }] } },
        }, AppErrorType.Validation);
        await ExpectChangeFailureAsync(host, subscription.Id, new SelectorChange
        {
            Selector = Sports with { Sources = new SelectorSources { Include = [new SelectorSourceRef { PipelineId = Guid.NewGuid() }] } },
        }, AppErrorType.Validation);
        await ExpectChangeFailureAsync(host, subscription.Id, new SelectorChange { Selector = Sports with { EntityTypes = ["movie"] } }, AppErrorType.Validation);
        await ExpectChangeFailureAsync(host, subscription.Id, new SelectorChange { Selector = Sports, ChangedBy = new string('x', 201) }, AppErrorType.Validation);
        await ExpectChangeFailureAsync(host, Guid.NewGuid(), new SelectorChange { Selector = Sports }, AppErrorType.NotFound);
        var channel = PostHost.Value(await host.Delivery.SubscribeAsync(new SubscriptionDeclaration
        {
            TenantId = host.TenantId, ContainerId = host.ContainerId, PipelineId = host.PipelineId,
        }));
        await ExpectChangeFailureAsync(host, channel.Id, new SelectorChange { Selector = Sports }, AppErrorType.Validation);
        PostHost.Value(await host.Delivery.SetStateAsync(subscription.Id, SubscriptionState.Archived));
        await ExpectChangeFailureAsync(host, subscription.Id, new SelectorChange { Selector = Sports }, AppErrorType.Conflict);

        var stored = PostHost.Value(await host.Delivery.GetSubscriptionAsync(subscription.Id));
        stored.SelectorVersion.Should().Be(1);
        stored.Selector.Should().BeEquivalentTo(Fashion);
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_subscription_selectors WHERE subscription_id=@id", ("id", subscription.Id))).Should().Be(1);
    }

    [Fact]
    public async Task Subscribe_ShouldRefuseUnknownTaxonomyKeys_AndForeignRefs()
    {
        await using var host = await PostHost.StartAsync();

        await ExpectFailureAsync(host, Declaration(host, Fashion with { Domains = [new SelectorDomain { Key = "no-such-domain" }] }), AppErrorType.Validation);
        await ExpectFailureAsync(host, Declaration(host, Fashion with
        {
            Domains = [new SelectorDomain { Key = "style-fashion", Except = ["style-fashion/no-such-style"] }],
        }), AppErrorType.Validation);
        await ExpectFailureAsync(host, Declaration(host, Fashion with { ExcludeDomains = ["no-such-domain"] }), AppErrorType.Validation);
        await ExpectFailureAsync(host, Declaration(host, Fashion with
        {
            Sources = new SelectorSources { Include = [new SelectorSourceRef { SourceId = Guid.NewGuid() }] },
        }), AppErrorType.Validation);
        // An active node passes in each of the three places a domain key can sit.
        PostHost.Value(await host.Delivery.SubscribeAsync(Declaration(host, Fashion with
        {
            Domains = [new SelectorDomain { Key = "style-fashion", Except = ["style-fashion/beauty"] }], ExcludeDomains = ["sports"],
        })));

        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_subscriptions")).Should().Be(1, "only the valid declaration was written");
    }

    [Fact]
    public async Task SetState_ShouldStopEvents_AndKeepArchivedForGood()
    {
        await using var host = await PostHost.StartAsync();
        var subscription = PostHost.Value(await host.Delivery.SubscribeAsync(Declaration(host, Fashion)));
        var first = await SaveAsync(host, "first", "A fashion week.", FashionTags);

        PostHost.Value(await host.Delivery.SetStateAsync(subscription.Id, SubscriptionState.Suspended)).State.Should().Be(SubscriptionState.Suspended);
        await SaveAsync(host, "while-suspended", "A fashion week while suspended.", FashionTags);
        PostHost.Value(await host.Delivery.SetStateAsync(subscription.Id, SubscriptionState.Suspended)).State.Should().Be(SubscriptionState.Suspended);
        PostHost.Value(await host.Delivery.SetStateAsync(subscription.Id, SubscriptionState.Active)).State.Should().Be(SubscriptionState.Active);
        var resumed = await SaveAsync(host, "resumed", "A fashion week after resuming.", FashionTags);
        (await host.PullUntilAsync(subscription.Id, page => page.Data.Count >= 2)).Data.Select(item => item.PostId)
            .Should().Equal(first.Post.Id, resumed.Post.Id);

        PostHost.Value(await host.Delivery.SetStateAsync(subscription.Id, SubscriptionState.Archived)).State.Should().Be(SubscriptionState.Archived);
        await SaveAsync(host, "after-archive", "A fashion week after archiving.", FashionTags);
        PostHost.Value(await host.Delivery.SetStateAsync(subscription.Id, SubscriptionState.Archived)).State.Should().Be(SubscriptionState.Archived);
        PostHost.Value(await host.Delivery.PullAsync(subscription.Id)).Data.Should().HaveCount(2);
        (await host.Delivery.SetStateAsync(subscription.Id, SubscriptionState.Active)).Should().BeOfType<Result<SubscriptionModel>.Failure>()
            .Which.Error.Type.Should().Be(AppErrorType.Conflict);
        (await host.Delivery.SetStateAsync(subscription.Id, SubscriptionState.Suspended)).Should().BeOfType<Result<SubscriptionModel>.Failure>()
            .Which.Error.Type.Should().Be(AppErrorType.Conflict);
        (await host.Delivery.SetStateAsync(Guid.NewGuid(), SubscriptionState.Active)).Should().BeOfType<Result<SubscriptionModel>.Failure>()
            .Which.Error.Type.Should().Be(AppErrorType.NotFound);
        (await host.Delivery.SetStateAsync(subscription.Id, (SubscriptionState)99)).Should().BeOfType<Result<SubscriptionModel>.Failure>()
            .Which.Error.Type.Should().Be(AppErrorType.Validation);

        var again = PostHost.Value(await host.Delivery.SubscribeAsync(Declaration(host, Sports)));
        again.Name.Should().Be("ocharo-events", "an archived subscription frees its name");
    }

    [Fact]
    public async Task Pull_ShouldRenderMatchAndPayloadContext_ForSelectorEvents()
    {
        await using var host = await PostHost.StartAsync();
        var group = PostHost.Value(await host.Registry.DeclareSourceGroupAsync(new SourceGroupDeclaration { ContainerId = host.ContainerId, Name = "Afisha" }));
        PostHost.Value(await host.Registry.UpdateSourceAsync(host.SourceId, new SourceUpdate { GroupId = group.Id }));
        var selector = Fashion with
        {
            Sources = new SelectorSources
            {
                Include =
                [
                    new SelectorSourceRef { GroupId = group.Id, Context = new Dictionary<string, string> { ["defaultKind"] = "exhibition", ["region"] = "tashkent" } },
                    new SelectorSourceRef { SourceId = host.SourceId, Context = new Dictionary<string, string> { ["defaultKind"] = "trade-fair" } },
                    new SelectorSourceRef { PipelineId = host.PipelineId, Context = new Dictionary<string, string> { ["section"] = "main" } },
                ],
            },
        };
        var subscription = PostHost.Value(await host.Delivery.SubscribeAsync(Declaration(host, selector) with { Schema = PostEnvelopeConstants.EventSchema }));
        var channel = PostHost.Value(await host.Delivery.SubscribeAsync(new SubscriptionDeclaration
        {
            TenantId = host.TenantId, ContainerId = host.ContainerId, PipelineId = host.PipelineId, Schema = PostEnvelopeConstants.EventSchema,
        }));
        var saved = await SaveAsync(host, "week", "A fashion week.", FashionTags);

        var page = await host.PullUntilAsync(subscription.Id, page => page.Data.Count >= 1);

        var item = page.Data.Should().ContainSingle().Subject;
        item.Match!.Verdict.Should().Be(SelectorVerdict.Match);
        item.Match.SelectorVersion.Should().Be(1);
        item.Context.Should().Equal(new Dictionary<string, string> { ["catalog"] = "ocharo-fashion" }, "the event's own context stays the subscription's");
        var context = item.Data!.Value.GetProperty("context");
        context.GetProperty("catalog").GetString().Should().Be("ocharo-fashion");
        context.GetProperty("defaultKind").GetString().Should().Be("trade-fair", "a source ref wins over its group's");
        context.GetProperty("region").GetString().Should().Be("tashkent");
        context.GetProperty("section").GetString().Should().Be("main");
        item.PostId.Should().Be(saved.Post.Id);

        var channelEvent = (await host.PullUntilAsync(channel.Id, page => page.Data.Count >= 1)).Data.Should().ContainSingle().Subject;
        channelEvent.Match.Should().BeNull();
        channelEvent.Data!.Value.TryGetProperty("context", out _).Should().BeFalse("a channel event renders as it always did");

        await ReviseAsync(host, "week", "A marathon.", SportsTags);
        var unmatched = (await host.PullUntilAsync(subscription.Id, page => page.Data.Count >= 1, page.Next)).Data.Should().ContainSingle().Subject;
        unmatched.Type.Should().Be(DeliveryEventTypes.PostUnmatched);
        unmatched.Match!.Verdict.Should().Be(SelectorVerdict.NoMatch);
        unmatched.Data!.Value.GetProperty("context").GetProperty("defaultKind").GetString().Should().Be("trade-fair");
    }

    [Fact]
    public async Task Hooks_ShouldRunAfterCommit_WithTheWindow()
    {
        await using var host = await PostHost.StartAsync();
        var failing = new RecordingHook(host.ConnectionString, fail: true);
        var recording = new RecordingHook(host.ConnectionString);
        await using var scope = Open(host, failing, recording);

        var created = PostHost.Value(await scope.Delivery.SubscribeAsync(Declaration(host, Fashion)));
        var bound = host.Clock.GetUtcNow().AddDays(-3);
        var since = PostHost.Value(await scope.Delivery.SubscribeAsync(Declaration(host, Sports) with
        {
            Name = "since-sports", Backfill = BackfillMode.Since, BackfillSinceUtc = bound,
        }));
        var window = host.Clock.GetUtcNow().AddDays(-7);
        PostHost.Value(await scope.Delivery.SetSelectorAsync(created.Id, new SelectorChange { Selector = Sports, WindowSinceUtc = window }));
        PostHost.Value(await scope.Delivery.SetSelectorAsync(created.Id, new SelectorChange { Selector = Sports, WindowSinceUtc = window }));
        (await scope.Delivery.SetSelectorAsync(created.Id, new SelectorChange { Selector = Fashion, WindowSinceUtc = host.Clock.GetUtcNow().AddDays(-400) }))
            .Should().BeOfType<Result<SelectorChangeModel>.Failure>();

        failing.Calls.Select(call => call.SubscriptionId).Should().Equal(created.Id, since.Id, created.Id);
        recording.Calls.Should().Equal(
            new HookCall(created.Id, null, 1),
            new HookCall(since.Id, bound, 1),
            new HookCall(created.Id, window, 2));
    }

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
    private static async Task<CaptureSaveResult> SaveAsync(PostHost host, string key, string text, string[]? tags = null, Guid? sourceId = null, string domain = "mstar.uz") =>
        PostHost.Value(await host.Posts.SaveCaptureAsync(host.ContainerId, sourceId ?? host.SourceId, null, PostStoreTests.Capture(host, key, text, domain),
            Tagged($"Title of {key}", text, tags ?? [])));

    /// <summary>Saves a later, different capture of the same link, which raises the post's revision.</summary>
    private static async Task<CaptureSaveResult> ReviseAsync(PostHost host, string key, string text, string[] tags)
    {
        host.Clock.Advance(TimeSpan.FromHours(1));
        return await SaveAsync(host, key, text, tags);
    }

    private static string Item(Guid postId, int revision, string type, string suffix = "") =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{postId:N}:{revision}:{type}{suffix}"))).ToLowerInvariant();

    private static async Task ExpectFailureAsync(PostHost host, SubscriptionDeclaration declaration, AppErrorType type) =>
        (await host.Delivery.SubscribeAsync(declaration)).Should().BeOfType<Result<SubscriptionModel>.Failure>().Which.Error.Type.Should().Be(type);

    private static async Task ExpectChangeFailureAsync(PostHost host, Guid subscriptionId, SelectorChange change, AppErrorType type) =>
        (await host.Delivery.SetSelectorAsync(subscriptionId, change)).Should().BeOfType<Result<SelectorChangeModel>.Failure>().Which.Error.Type.Should().Be(type);

    private static async Task<CursorPage<DeliveryEvent>> PullUntilAsync(IDeliveryBroker delivery, Guid subscriptionId, Func<CursorPage<DeliveryEvent>, bool> ready, string? cursor = null)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            var page = PostHost.Value(await delivery.PullAsync(subscriptionId, cursor));
            if (ready(page) || elapsed.Elapsed > TimeSpan.FromSeconds(5)) return page;
            await Task.Delay(50);
        }
    }

    private static HookScope Open(PostHost host, params ISelectorChangeHook[] hooks)
    {
        var services = new ServiceCollection().AddLogging().AddSingleton<TimeProvider>(host.Clock);
        foreach (var hook in hooks)
            services.AddSingleton(hook);
        services.AddHarvest().Configure(options => options.ConnectionString = host.ConnectionString);
        services.AddHarvestPosts().AddHarvestDelivery();
        var provider = services.BuildServiceProvider(validateScopes: true);
        return new HookScope(provider, provider.CreateAsyncScope());
    }

    private sealed class HookScope(ServiceProvider provider, AsyncServiceScope scope) : IAsyncDisposable
    {
        internal IDeliveryBroker Delivery => scope.ServiceProvider.GetRequiredService<IDeliveryBroker>();
        public async ValueTask DisposeAsync() { await scope.DisposeAsync(); await provider.DisposeAsync(); }
    }

    /// <summary>One call a hook saw: the subscription, the window, and the selector version the store held when it ran.</summary>
    private sealed record HookCall(Guid SubscriptionId, DateTimeOffset? Window, int? StoredVersion);

    /// <summary>Records each call with the selector version a separate connection reads, so it proves the change committed first.</summary>
    private sealed class RecordingHook(string connectionString, bool fail = false) : ISelectorChangeHook
    {
        internal List<HookCall> Calls { get; } = [];

        public async Task OnChangedAsync(Guid subscriptionId, DateTimeOffset? windowSinceUtc, CancellationToken cancellationToken)
        {
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync(cancellationToken);
            await using var command = new NpgsqlCommand("SELECT selector_version FROM harvest_subscriptions WHERE id=@id", connection);
            command.Parameters.AddWithValue("id", subscriptionId);
            var version = await command.ExecuteScalarAsync(cancellationToken);
            Calls.Add(new HookCall(subscriptionId, windowSinceUtc, version as int?));
            if (fail) throw new InvalidOperationException("The hook failed.");
        }
    }
}
