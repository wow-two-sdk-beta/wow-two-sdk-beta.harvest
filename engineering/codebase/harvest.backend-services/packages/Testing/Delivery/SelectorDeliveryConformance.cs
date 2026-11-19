using System.Security.Cryptography;
using System.Text;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using Xunit;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Testing.Delivery;

/// <summary>Specifies selector subscriptions shared by the in-process module, HTTP form and consumer fakes: what a selector
/// delivers, the verdict each event carries, the change window on the same cursor, and the state machine. Posts carry their
/// Layer 1 tags in the capture's envelope, as a stored answer writes them.</summary>
/// <remarks>Taxonomy existence is the module's alone to check, so no case here refuses an unknown domain key; the keys the
/// cases use are active nodes of the module's taxonomy.</remarks>
public abstract class SelectorDeliveryConformance
{
    private const string Beauty = "style-fashion/beauty";
    private const string SubscriptionName = "conformance-events";

    private static readonly string[] FashionTags = ["type:event", "domain:style-fashion/high-fashion", "domain:style-fashion", "topic:fashion-week"];
    private static readonly string[] SportsTags = ["type:event", "domain:sports", "topic:marathon"];
    private static readonly string[] ListingTags = ["type:listing", "domain:style-fashion/high-fashion", "domain:style-fashion"];
    private static readonly string[] BeautyTags = ["type:event", "domain:" + Beauty, "domain:style-fashion", "topic:cosmetics"];
    private static readonly string[] BeautyShowTags = [.. BeautyTags, "topic:fashion-show"];

    private static readonly SubscriptionSelector Fashion = new() { EntityTypes = ["event"], Domains = [new SelectorDomain { Key = "style-fashion" }] };
    private static readonly SubscriptionSelector Sports = new() { EntityTypes = ["event"], Domains = [new SelectorDomain { Key = "sports" }] };

    /// <summary>Gets the registry that creates each case's isolated tenant and container.</summary>
    protected abstract IRegistrationBroker Registry { get; }
    /// <summary>Gets the post implementation under test.</summary>
    protected abstract IPostBroker Posts { get; }
    /// <summary>Gets the delivery implementation under test.</summary>
    protected abstract IDeliveryBroker Delivery { get; }
    /// <summary>Gets the implementation's clock.</summary>
    protected abstract TimeProvider Clock { get; }

    /// <summary>A selector backfills the posts that carry the type and a domain at or under it, and later writes of such
    /// posts arrive as <c>post.new</c> with the verdict, the selector version and the subscription's context.</summary>
    [Fact]
    public async Task Subscribe_ShouldDeliverTaggedMatches_WhenTypeAndDomainAgree()
    {
        var context = await ContextAsync();
        var before = await SaveAsync(context, "before", FashionTags);
        await SaveAsync(context, "sports", SportsTags);
        await SaveAsync(context, "listing", ListingTags);
        await SaveAsync(context, "untagged");

        var subscription = Value(await Delivery.SubscribeAsync(Declaration(context, Fashion)));
        var after = await SaveAsync(context, "after", FashionTags);

        subscription.Kind.Should().Be(SubscriptionKind.Selector);
        subscription.PipelineId.Should().BeNull();
        subscription.Name.Should().Be(SubscriptionName);
        subscription.SelectorVersion.Should().Be(1);
        subscription.Selector.Should().BeEquivalentTo(Fashion);
        subscription.State.Should().Be(SubscriptionState.Active);
        Value(await Delivery.GetSubscriptionAsync(subscription.Id)).Selector.Should().BeEquivalentTo(Fashion);
        Value(await Delivery.ListSubscriptionsAsync(context.Tenant)).Should().ContainSingle(item => item.Id == subscription.Id);
        var page = await PollAsync(subscription.Id, page => page.Data.Count >= 2);
        page.Data.Should().HaveCount(2);
        page.Data.Select(item => item.PostId).Should().Equal(before.Post.Id, after.Post.Id);
        page.Data.Select(item => item.Reason).Should().Equal("backfill", "ingest");
        page.Data.Should().OnlyContain(item => item.Type == DeliveryEventTypes.PostNew && item.Revision == 1 && item.PipelineId == null);
        page.Data.Should().OnlyContain(item => item.Context["catalog"] == "ocharo-fashion");
        page.Data.Should().OnlyContain(item => item.Match != null && item.Match.SelectorVersion == 1 && item.Match.Verdict == SelectorVerdict.Match);
        page.Data.Should().OnlyContain(item => item.Match!.Clauses.SequenceEqual(new[] { "type:event", "domain:style-fashion" }));
        page.Data.Select(item => item.ItemId).Should().Equal(Item(before.Post.Id, 1, DeliveryEventTypes.PostNew), Item(after.Post.Id, 1, DeliveryEventTypes.PostNew));
    }

    /// <summary>A post the selector cannot judge yet sends nothing; the revision that brings its tags arrives as
    /// <c>post.new</c>, not <c>post.changed</c>.</summary>
    [Fact]
    public async Task Subscribe_ShouldHoldUntaggedPosts_UntilTaggedRevisionArrivesAsPostNew()
    {
        var context = await ContextAsync();
        var subscription = Value(await Delivery.SubscribeAsync(Declaration(context, Fashion)));
        var waiting = await SaveAsync(context, "waiting");
        Value(await Delivery.PullAsync(subscription.Id)).Data.Should().BeEmpty("a post without tags is pending, and pending sends nothing");

        var tagged = await SaveAsync(context, "waiting", FashionTags, revision: 1);

        tagged.Post.Id.Should().Be(waiting.Post.Id);
        tagged.Post.Revision.Should().Be(2);
        var page = await PollAsync(subscription.Id, page => page.Data.Count >= 1);
        var item = page.Data.Should().ContainSingle().Subject;
        item.Type.Should().Be(DeliveryEventTypes.PostNew);
        item.PostId.Should().Be(waiting.Post.Id);
        item.Revision.Should().Be(2);
        item.Reason.Should().Be("ingest");
        item.Match!.Verdict.Should().Be(SelectorVerdict.Match);
        item.Match.Clauses.Should().Equal("type:event", "domain:style-fashion");
        item.ItemId.Should().Be(Item(waiting.Post.Id, 2, DeliveryEventTypes.PostNew));
    }

    /// <summary>The beauty rule: an <c>except</c> on a domain include holds a post whose most specific node sits under it,
    /// unless a topic term of the selector matches; deleting that topic sends the post away once, and restoring it brings it
    /// back as <c>post.changed</c>.</summary>
    [Fact]
    public async Task Selector_ShouldExceptBeauty_UnlessAClothingTopicMatches()
    {
        var context = await ContextAsync();
        var selector = Fashion with { Domains = [new SelectorDomain { Key = "style-fashion", Except = [Beauty] }], Topics = ["fashion-show"] };
        var subscription = Value(await Delivery.SubscribeAsync(Declaration(context, selector)));

        await SaveAsync(context, "beauty-expo", BeautyTags);
        var show = await SaveAsync(context, "beauty-show", BeautyShowTags);
        var plain = await SaveAsync(context, "high-fashion", FashionTags);

        var first = await PollAsync(subscription.Id, page => page.Data.Count >= 2);
        first.Data.Select(item => item.PostId).Should().Equal(show.Post.Id, plain.Post.Id);
        first.Data[0].Match!.Clauses.Should().Equal("type:event", "topic:fashion-show");
        first.Data[1].Match!.Clauses.Should().Equal("type:event", "domain:style-fashion");
        first.Data.Should().HaveCount(2, "the beauty expo with no clothing topic is held");

        await SaveAsync(context, "beauty-show", BeautyTags, revision: 1);
        await SaveAsync(context, "beauty-show", BeautyTags, revision: 2);
        await SaveAsync(context, "beauty-show", BeautyShowTags, revision: 3);

        var next = await PollAsync(subscription.Id, page => page.Data.Count >= 2, first.Next);
        next.Data.Select(item => item.Type).Should().Equal(DeliveryEventTypes.PostUnmatched, DeliveryEventTypes.PostChanged);
        next.Data.Should().OnlyContain(item => item.PostId == show.Post.Id);
        next.Data.Select(item => item.Revision).Should().Equal(2, 4);
        next.Data[0].Match!.Verdict.Should().Be(SelectorVerdict.NoMatch);
        next.Data[0].Match!.Clauses.Should().Equal("except:" + Beauty);
        next.Data[1].Match!.Verdict.Should().Be(SelectorVerdict.Match);
        next.Data[1].Match!.Clauses.Should().Equal("type:event", "topic:fashion-show");
    }

    /// <summary>A source ref names a group, a source or a section; an exclude wins over an include, each post is judged per
    /// link, and the contexts of the include refs that matched the post's link render as the payload's top-level
    /// <c>context</c>, later levels winning on a shared key.</summary>
    [Fact]
    public async Task Selector_ShouldLetSourceExcludeWin_AndRenderRefContext()
    {
        var context = await ContextAsync();
        var second = await SourceAsync(context, "second");
        var third = await SourceAsync(context, "third");
        var group = Value(await Registry.DeclareSourceGroupAsync(new SourceGroupDeclaration { ContainerId = context.Container, Name = "Publishers" }));
        foreach (var source in new[] { context.Source, second })
            Value(await Registry.UpdateSourceAsync(source, new SourceUpdate { GroupId = group.Id }));
        var selector = Fashion with
        {
            Sources = new SelectorSources
            {
                Include =
                [
                    new SelectorSourceRef { GroupId = group.Id, Context = new Dictionary<string, string> { ["defaultKind"] = "exhibition", ["region"] = "tashkent" } },
                    new SelectorSourceRef { SourceId = context.Source, Context = new Dictionary<string, string> { ["defaultKind"] = "trade-fair" } },
                    new SelectorSourceRef { PipelineId = context.Pipeline, Context = new Dictionary<string, string> { ["section"] = "main" } },
                ],
                Exclude = [new SelectorSourceRef { SourceId = second }],
            },
        };
        var subscription = Value(await Delivery.SubscribeAsync(Declaration(context, selector)));

        var included = await SaveAsync(context, "included", FashionTags);
        await SaveAsync(context, "excluded", FashionTags, source: second);
        await SaveAsync(context, "outside", FashionTags, source: third);
        var origin = "urn:event:" + Guid.NewGuid().ToString("N");
        await SaveAsync(context, "shared-excluded", FashionTags, source: second, origin: origin);
        var shared = await SaveAsync(context, "shared-included", FashionTags, origin: origin);

        var page = await PollAsync(subscription.Id, page => page.Data.Count >= 2);
        page.Data.Should().HaveCount(2, "a post reaches the selector through its included, not excluded, link");
        page.Data.Select(item => item.PostId).Should().Equal(included.Post.Id, shared.Post.Id);
        page.Data.Should().OnlyContain(item => item.Type == DeliveryEventTypes.PostNew);
        foreach (var item in page.Data)
        {
            item.Context.Should().Equal(new Dictionary<string, string> { ["catalog"] = "ocharo-fashion" }, "the event's own context stays the subscription's");
            var rendered = item.Data!.Value.GetProperty("context");
            rendered.GetProperty("catalog").GetString().Should().Be("ocharo-fashion");
            rendered.GetProperty("defaultKind").GetString().Should().Be("trade-fair", "a source ref wins over its group's");
            rendered.GetProperty("region").GetString().Should().Be("tashkent");
            rendered.GetProperty("section").GetString().Should().Be("main");
        }
    }

    /// <summary>Changing the selector re-judges the window on the subscription's own cursor: a held post that no longer
    /// matches leaves as <c>post.unmatched</c>, a post that now matches enters as <c>post.new</c>, and an untagged post is
    /// counted as pending.</summary>
    [Fact]
    public async Task SetSelector_ShouldBackfillAndUnmatchOnTheSameCursor()
    {
        var context = await ContextAsync();
        var leaving = await SaveAsync(context, "leaving", FashionTags);
        var joining = await SaveAsync(context, "joining", SportsTags);
        await SaveAsync(context, "waiting");
        var subscription = Value(await Delivery.SubscribeAsync(Declaration(context, Fashion)));
        var first = await PollAsync(subscription.Id, page => page.Data.Count >= 1);
        first.Data.Should().ContainSingle().Which.PostId.Should().Be(leaving.Post.Id);

        var change = Value(await Delivery.SetSelectorAsync(subscription.Id, new SelectorChange { Selector = Sports, ChangedBy = "conformance" }));

        change.Changed.Should().BeTrue();
        change.Subscription.SelectorVersion.Should().Be(2);
        change.Subscription.Selector.Should().BeEquivalentTo(Sports);
        change.WindowSinceUtc.Should().BeCloseTo(Clock.GetUtcNow().AddDays(-SelectorRules.DefaultWindowDays), TimeSpan.FromMinutes(5));
        (change.Considered, change.NewMatches, change.Unmatched, change.Rematched, change.Pending).Should().Be((3, 1, 1, 0, 1));
        var page = await PollAsync(subscription.Id, page => page.Data.Count >= 2, first.Next);
        page.Data.Should().HaveCount(2);
        var unmatched = page.Data.Single(item => item.PostId == leaving.Post.Id);
        var added = page.Data.Single(item => item.PostId == joining.Post.Id);
        unmatched.Type.Should().Be(DeliveryEventTypes.PostUnmatched);
        unmatched.Reason.Should().Be("resync");
        unmatched.Match!.Verdict.Should().Be(SelectorVerdict.NoMatch);
        unmatched.Match.Clauses.Should().Equal("no-subject");
        unmatched.ItemId.Should().Be(Item(leaving.Post.Id, 1, DeliveryEventTypes.PostUnmatched, ":s2"));
        added.Type.Should().Be(DeliveryEventTypes.PostNew);
        added.Reason.Should().Be("backfill");
        added.Match!.Verdict.Should().Be(SelectorVerdict.Match);
        added.Match.Clauses.Should().Equal("type:event", "domain:sports");
        added.ItemId.Should().Be(Item(joining.Post.Id, 1, DeliveryEventTypes.PostNew, ":s2"));
        page.Data.Should().OnlyContain(item => item.Match!.SelectorVersion == 2);
    }

    /// <summary>Leaving a selector and coming back to it at the same revision writes three events with three item ids, the
    /// change's version keeping each distinct.</summary>
    [Fact]
    public async Task SetSelector_ShouldRematchAnUnmatchedPost_WithADistinctItemId()
    {
        var context = await ContextAsync();
        var saved = await SaveAsync(context, "week", FashionTags);
        var subscription = Value(await Delivery.SubscribeAsync(Declaration(context, Fashion)));

        Value(await Delivery.SetSelectorAsync(subscription.Id, new SelectorChange { Selector = Sports }));
        var back = Value(await Delivery.SetSelectorAsync(subscription.Id, new SelectorChange { Selector = Fashion }));

        (back.Subscription.SelectorVersion, back.Rematched, back.NewMatches, back.Unmatched).Should().Be((3, 1, 0, 0));
        var page = await PollAsync(subscription.Id, page => page.Data.Count >= 3);
        page.Data.Select(item => item.Type).Should().Equal(DeliveryEventTypes.PostNew, DeliveryEventTypes.PostUnmatched, DeliveryEventTypes.PostChanged);
        page.Data.Select(item => item.Revision).Should().Equal(1, 1, 1);
        page.Data.Select(item => item.Reason).Should().Equal("backfill", "resync", "resync");
        page.Data.Select(item => item.Match!.SelectorVersion).Should().Equal(1, 2, 3);
        page.Data.Select(item => item.ItemId).Should().Equal(Item(saved.Post.Id, 1, DeliveryEventTypes.PostNew),
            Item(saved.Post.Id, 1, DeliveryEventTypes.PostUnmatched, ":s2"), Item(saved.Post.Id, 1, DeliveryEventTypes.PostChanged, ":s3"));
    }

    /// <summary>A selector equal to the stored one, once normalized, writes nothing: the version stays and no event follows.</summary>
    [Fact]
    public async Task SetSelector_ShouldKeepTheVersion_WhenUnchanged()
    {
        var context = await ContextAsync();
        await SaveAsync(context, "week", FashionTags);
        var subscription = Value(await Delivery.SubscribeAsync(Declaration(context, Fashion)));
        var first = await PollAsync(subscription.Id, page => page.Data.Count >= 1);

        var same = Value(await Delivery.SetSelectorAsync(subscription.Id, new SelectorChange
        {
            Selector = new SubscriptionSelector { EntityTypes = [" Event "], Domains = [new SelectorDomain { Key = "STYLE-FASHION" }] },
        }));

        same.Changed.Should().BeFalse();
        same.WindowSinceUtc.Should().BeNull();
        (same.Considered, same.NewMatches, same.Unmatched, same.Rematched, same.Pending).Should().Be((0, 0, 0, 0, 0));
        same.Subscription.SelectorVersion.Should().Be(1);
        Value(await Delivery.GetSubscriptionAsync(subscription.Id)).SelectorVersion.Should().Be(1);
        Value(await Delivery.PullAsync(subscription.Id, first.Next)).Data.Should().BeEmpty();
        Value(await Delivery.PullAsync(subscription.Id)).Data.Should().ContainSingle();
    }

    /// <summary>A selector subscription refuses what it cannot be: a pipeline or a filter beside the selector, a missing,
    /// malformed or taken name, a bad rule or source ref, and another tenant's container; nothing is written.</summary>
    [Fact]
    public async Task Subscribe_ShouldRefuseInvalidSelectors()
    {
        var context = await ContextAsync();
        var other = Value(await Registry.RegisterTenantAsync(new TenantRegistration { Slug = "other-" + context.Unique, Name = "Other" }));
        var foreign = Value(await Registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = other.Id }));
        var valid = Value(await Delivery.SubscribeAsync(Declaration(context, Fashion)));

        await RefusedAsync(Declaration(context, Fashion), AppErrorType.Conflict);
        await RefusedAsync(Declaration(context, Fashion, name: null), AppErrorType.Validation);
        await RefusedAsync(Declaration(context, Fashion, name: "Not A Slug"), AppErrorType.Validation);
        await RefusedAsync(Declaration(context, Fashion, name: "with-pipeline") with { PipelineId = context.Pipeline }, AppErrorType.Validation);
        await RefusedAsync(Declaration(context, Fashion, name: "with-filter") with { FilterKey = "tag:concert" }, AppErrorType.Validation);
        await RefusedAsync(Declaration(context, Fashion, name: "bad-since") with { Backfill = BackfillMode.Since }, AppErrorType.Validation);
        await RefusedAsync(Declaration(context, Fashion with { EntityTypes = ["movie"] }, name: "bad-type"), AppErrorType.Validation);
        await RefusedAsync(Declaration(context, Fashion with { Domains = [new SelectorDomain { Key = "Not A Key" }] }, name: "bad-key"), AppErrorType.Validation);
        await RefusedAsync(Declaration(context, Fashion with { Domains = [new SelectorDomain { Key = "style-fashion", Except = ["sports"] }] }, name: "bad-except"), AppErrorType.Validation);
        await RefusedAsync(Declaration(context, Fashion with { Topics = ["Not A Topic"] }, name: "bad-topic"), AppErrorType.Validation);
        await RefusedAsync(Declaration(context, Fashion with
        {
            Sources = new SelectorSources { Include = [new SelectorSourceRef { SourceId = context.Source, GroupId = Guid.NewGuid() }] },
        }, name: "two-ids"), AppErrorType.Validation);
        await RefusedAsync(Declaration(context, Fashion with
        {
            Sources = new SelectorSources { Exclude = [new SelectorSourceRef { SourceId = context.Source, Context = new Dictionary<string, string> { ["a"] = "b" } }] },
        }, name: "exclude-context"), AppErrorType.Validation);
        await RefusedAsync(Declaration(context, Fashion with
        {
            Sources = new SelectorSources { Include = [new SelectorSourceRef { SourceId = Guid.NewGuid() }] },
        }, name: "foreign-source"), AppErrorType.Validation);
        await RefusedAsync(Declaration(context, Fashion, name: "foreign-container") with { ContainerId = foreign.Id }, AppErrorType.NotFound);
        await RefusedAsync(Declaration(context, Fashion, name: "foreign-tenant") with { TenantId = Guid.NewGuid() }, AppErrorType.NotFound);
        await RefusedAsync(new SubscriptionDeclaration { TenantId = context.Tenant, ContainerId = context.Container, PipelineId = context.Pipeline, Name = "channel-with-a-name" },
            AppErrorType.Validation);
        await RefusedAsync(new SubscriptionDeclaration { TenantId = context.Tenant, ContainerId = context.Container }, AppErrorType.Validation);

        Value(await Delivery.ListSubscriptionsAsync(context.Tenant)).Should().ContainSingle().Which.Id.Should().Be(valid.Id);
    }

    /// <summary>A subscription moves between active and suspended and stops delivering while it is not active; archived
    /// is final, frees the name, and refuses a selector change.</summary>
    [Fact]
    public async Task SetState_ShouldStopDelivery_WhenArchived_AndRefuseReactivation()
    {
        var context = await ContextAsync();
        var subscription = Value(await Delivery.SubscribeAsync(Declaration(context, Fashion)));
        var first = await SaveAsync(context, "first", FashionTags);

        Value(await Delivery.SetStateAsync(subscription.Id, SubscriptionState.Suspended)).State.Should().Be(SubscriptionState.Suspended);
        await SaveAsync(context, "while-suspended", FashionTags);
        Value(await Delivery.SetStateAsync(subscription.Id, SubscriptionState.Suspended)).State.Should().Be(SubscriptionState.Suspended);
        Value(await Delivery.SetStateAsync(subscription.Id, SubscriptionState.Active)).State.Should().Be(SubscriptionState.Active);
        var resumed = await SaveAsync(context, "resumed", FashionTags);
        (await PollAsync(subscription.Id, page => page.Data.Count >= 2)).Data.Select(item => item.PostId).Should().Equal(first.Post.Id, resumed.Post.Id);

        Value(await Delivery.SetStateAsync(subscription.Id, SubscriptionState.Archived)).State.Should().Be(SubscriptionState.Archived);
        await SaveAsync(context, "after-archive", FashionTags);
        Value(await Delivery.SetStateAsync(subscription.Id, SubscriptionState.Archived)).State.Should().Be(SubscriptionState.Archived);
        Value(await Delivery.PullAsync(subscription.Id)).Data.Should().HaveCount(2);
        Error(await Delivery.SetStateAsync(subscription.Id, SubscriptionState.Active)).Type.Should().Be(AppErrorType.Conflict);
        Error(await Delivery.SetStateAsync(subscription.Id, SubscriptionState.Suspended)).Type.Should().Be(AppErrorType.Conflict);
        Error(await Delivery.SetStateAsync(Guid.NewGuid(), SubscriptionState.Active)).Type.Should().Be(AppErrorType.NotFound);
        Error(await Delivery.SetSelectorAsync(subscription.Id, new SelectorChange { Selector = Sports })).Type.Should().Be(AppErrorType.Conflict);
        Error(await Delivery.SetSelectorAsync(Guid.NewGuid(), new SelectorChange { Selector = Sports })).Type.Should().Be(AppErrorType.NotFound);
        Value(await Delivery.GetSubscriptionAsync(subscription.Id)).SelectorVersion.Should().Be(1);

        var channel = Value(await Delivery.SubscribeAsync(new SubscriptionDeclaration { TenantId = context.Tenant, ContainerId = context.Container, PipelineId = context.Pipeline }));
        Error(await Delivery.SetSelectorAsync(channel.Id, new SelectorChange { Selector = Sports })).Type.Should().Be(AppErrorType.Validation);
        var again = Value(await Delivery.SubscribeAsync(Declaration(context, Sports)));
        again.Name.Should().Be(SubscriptionName, "an archived subscription frees its name");
    }

    private async Task<Context> ContextAsync()
    {
        var unique = Guid.NewGuid().ToString("N");
        var tenant = Value(await Registry.RegisterTenantAsync(new TenantRegistration { Slug = "selector-" + unique, Name = "Selector conformance" }));
        var container = Value(await Registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = tenant.Id }));
        var source = Value(await Registry.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = container.Id, Family = SourceFamily.Site, Handle = unique + ".example", Coverage = SourceCoverage.Whole,
            Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch,
        }));
        var pipeline = Value(await Registry.DeclarePipelineAsync(new PipelineDeclaration
        {
            ContainerId = container.Id, SourceId = source.Id,
            Schedules = [new ScheduleDeclaration { Kind = WorkKind.Capture, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(1) }],
        }));
        return new Context(unique, tenant.Id, container.Id, source.Id, pipeline.Id);
    }

    private async Task<Guid> SourceAsync(Context context, string name) =>
        Value(await Registry.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = context.Container, Family = SourceFamily.Site, Handle = $"{name}.{context.Unique}.example", Coverage = SourceCoverage.Whole,
            Completeness = SourceCompleteness.Complete, Posture = SourcePosture.OwnFetch,
        })).Id;

    /// <summary>Saves one capture whose envelope carries <paramref name="tags"/> as the Layer 1 answer, none leaving it
    /// untagged; a higher <paramref name="revision"/> is a later, different capture of the same link.</summary>
    private async Task<CaptureSaveResult> SaveAsync(Context context, string key, string[]? tags = null, int revision = 0, Guid? source = null, string? origin = null)
    {
        var projection = revision == 0 ? key : $"{key} revision {revision}";
        var capture = new Capture
        {
            ExternalKey = key, Url = $"https://fixture.example/{key}", RequestUrl = $"https://fixture.example/{key}", FinalUrl = $"https://fixture.example/{key}",
            CapturedAtUtc = Clock.GetUtcNow().ToUniversalTime().AddSeconds(revision), Body = Encoding.UTF8.GetBytes(projection), ContentProjection = projection,
            ContentType = "text/html", HttpStatus = 200, OriginKey = origin,
        };
        return Value(await Posts.SaveCaptureAsync(context.Container, source ?? context.Source, null, capture, new PostEnvelope
        {
            Kind = PostKind.Event, Title = $"Title of {key}", Text = projection,
            Tags = [.. (tags ?? []).Select(tag => new PostTag { Tag = tag, Tier = ExtractionTier.SmallModel })],
            Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
        }));
    }

    private async Task<CursorPage<DeliveryEvent>> PollAsync(Guid subscriptionId, Func<CursorPage<DeliveryEvent>, bool> ready, string? cursor = null, int limit = 100)
    {
        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        while (true)
        {
            var page = Value(await Delivery.PullAsync(subscriptionId, cursor, limit));
            if (ready(page) || elapsed.Elapsed > TimeSpan.FromSeconds(5)) return page;
            await Task.Delay(50);
        }
    }

    private async Task RefusedAsync(SubscriptionDeclaration declaration, AppErrorType type) =>
        Error(await Delivery.SubscribeAsync(declaration)).Type.Should().Be(type, $"a declaration named '{declaration.Name}' is refused");

    private static SubscriptionDeclaration Declaration(Context context, SubscriptionSelector selector, string? name = SubscriptionName) => new()
    {
        TenantId = context.Tenant, ContainerId = context.Container, Name = name, Selector = selector,
        Context = new Dictionary<string, string> { ["catalog"] = "ocharo-fashion" },
    };

    private static string Item(Guid postId, int revision, string type, string suffix = "") =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{postId:N}:{revision}:{type}{suffix}"))).ToLowerInvariant();

    private static T Value<T>(Result<T> result) where T : notnull => result is Result<T>.Success success ? success.Value : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);
    private static AppError Error<T>(Result<T> result) where T : notnull => result is Result<T>.Failure failure ? failure.Error : throw new Xunit.Sdk.XunitException("Expected failure.");
    private sealed record Context(string Unique, Guid Tenant, Guid Container, Guid Source, Guid Pipeline);
}
