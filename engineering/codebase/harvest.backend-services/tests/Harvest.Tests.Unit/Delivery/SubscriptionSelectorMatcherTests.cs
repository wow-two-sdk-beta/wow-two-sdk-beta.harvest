using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

namespace Harvest.Tests.Unit.Delivery;

public sealed class SubscriptionSelectorMatcherTests
{
    private static readonly Guid GroupId = Guid.Parse("0f000000-0000-0000-0000-000000000001");
    private static readonly Guid SourceId = Guid.Parse("0f000000-0000-0000-0000-000000000002");
    private static readonly Guid OtherSourceId = Guid.Parse("0f000000-0000-0000-0000-000000000003");
    private static readonly Guid PipelineId = Guid.Parse("0f000000-0000-0000-0000-000000000004");
    private static readonly Guid LinkId = Guid.Parse("0f000000-0000-0000-0000-000000000011");
    private static readonly Guid OtherLinkId = Guid.Parse("0f000000-0000-0000-0000-000000000012");
    private static readonly IReadOnlyDictionary<Guid, SelectorSection> NoSections = new Dictionary<Guid, SelectorSection>();
    private static readonly IReadOnlyDictionary<string, string> NoContext = new Dictionary<string, string>();

    private static readonly string[] LifecycleTypes =
    [
        DeliveryEventTypes.LinkState, DeliveryEventTypes.PostMerged, DeliveryEventTypes.PostUnmerged,
        DeliveryEventTypes.PostPurged, DeliveryEventTypes.PostRelisted,
    ];

    private static readonly string[] ContentTypes = [DeliveryEventTypes.PostNew, DeliveryEventTypes.PostChanged, DeliveryEventTypes.PostExtracted];

    [Fact]
    public void Evaluate_ShouldMatch_WhenTypeAndDomainAgree()
    {
        var result = Judge(Fashion(), Post("type:event", "domain:style-fashion/high-fashion", "domain:style-fashion"));

        Expect(result, SelectorVerdict.Match, "type:event", "domain:style-fashion");
    }

    [Fact]
    public void Evaluate_ShouldReadTheMostSpecificNode_WhenAncestorsAreTagged()
    {
        var result = Judge(Fashion(), Post("type:event", "domain:style-fashion", "domain:style-fashion/beauty"));

        Expect(result, SelectorVerdict.NoMatch, "except:style-fashion/beauty");
    }

    [Fact]
    public void Evaluate_ShouldHoldABeautyExpo_WhenNoClothingTopic()
    {
        var result = Judge(Fashion(), Post("type:event", "domain:style-fashion/beauty", "topic:cosmetics", "topic:skin-care"));

        Expect(result, SelectorVerdict.NoMatch, "except:style-fashion/beauty");
    }

    [Fact]
    public void Evaluate_ShouldPassABeautyExpo_WhenAClothingTopicMatches()
    {
        var result = Judge(Fashion(), Post("type:event", "domain:style-fashion/beauty", "topic:cosmetics", "topic:fashion-show"));

        Expect(result, SelectorVerdict.Match, "type:event", "topic:fashion-show");
    }

    [Fact]
    public void Evaluate_ShouldPassABeautyExpo_WhenAClothingNodeSitsBesideIt()
    {
        var result = Judge(Fashion(), Post("type:event", "domain:style-fashion/beauty", "domain:style-fashion/womens-fashion"));

        Expect(result, SelectorVerdict.Match, "type:event", "domain:style-fashion");
    }

    [Theory]
    [InlineData("designer-clothing", "clothing", true)]
    [InlineData("family-fashion-show", "fashion-show", true)]
    [InlineData("textiles", "textile", false)]
    [InlineData("clothingstore", "clothing", false)]
    [InlineData("show-fashion", "fashion-show", false)]
    [InlineData("fashion-big-show", "fashion-show", false)]
    public void Evaluate_ShouldMatchWholeTokens_InCompoundTopics(string topic, string term, bool matches)
    {
        var selector = new SubscriptionSelector { EntityTypes = ["event"], Topics = [term] };

        var result = Judge(selector, Post("type:event", "topic:" + topic));

        if (matches)
            Expect(result, SelectorVerdict.Match, "type:event", "topic:" + term);
        else
            Expect(result, SelectorVerdict.NoMatch, "no-subject");
        SelectorRules.TokenMatch(topic, term).Should().Be(matches);
    }

    [Fact]
    public void Evaluate_ShouldBePending_WhenUntagged()
    {
        Expect(Judge(Fashion(), Post()), SelectorVerdict.Pending, "untagged");
    }

    [Fact]
    public void Evaluate_ShouldReadOnlySmallModelTags()
    {
        var post = Post(PostKind.Event, [new PostTag { Tag = "type:event", Tier = ExtractionTier.Rules }, new PostTag { Tag = "domain:style-fashion", Tier = ExtractionTier.Deterministic }]);

        Expect(Judge(Fashion(), post), SelectorVerdict.Pending, "untagged");
    }

    [Fact]
    public void Evaluate_ShouldBePending_WhenTheTypeIsUnsure()
    {
        Expect(Judge(Fashion(), Post("domain:style-fashion/high-fashion")), SelectorVerdict.Pending, "unsure-type");
    }

    [Theory]
    [InlineData("listing")]
    [InlineData("none")]
    public void Evaluate_ShouldRefuseOtherEntityTypes(string type)
    {
        Expect(Judge(Fashion(), Post("type:" + type, "domain:style-fashion")), SelectorVerdict.NoMatch, "no-type:" + type);
    }

    [Fact]
    public void Evaluate_ShouldVetoExcludedDomainsAndTopics()
    {
        var selector = Fashion() with { ExcludeDomains = ["sports"], ExcludeTopics = ["marathon"] };

        Expect(Judge(selector, Post("type:event", "domain:style-fashion", "domain:sports/running")), SelectorVerdict.NoMatch, "exclude-domain:sports");
        Expect(Judge(selector, Post("type:event", "domain:style-fashion", "topic:city-marathon")), SelectorVerdict.NoMatch, "exclude-topic:marathon");
    }

    [Fact]
    public void Evaluate_ShouldVetoAnExcludedTimeframe()
    {
        var selector = Fashion() with { ExcludeTimeframes = ["past"] };

        Expect(Judge(selector, Post("type:event", "domain:style-fashion", "timeframe:past")), SelectorVerdict.NoMatch, "exclude-timeframe:past");
        Expect(Judge(selector, Post("type:event", "domain:style-fashion", "timeframe:upcoming")), SelectorVerdict.Match, "type:event", "domain:style-fashion");
        Expect(Judge(selector, Post("type:event", "domain:style-fashion", "timeframe:unsure")), SelectorVerdict.Match, "type:event", "domain:style-fashion");
    }

    [Fact]
    public void Evaluate_ShouldIgnoreTheTimeframe_WhenNoTagCarriesIt()
    {
        var selector = Fashion() with { ExcludeTimeframes = ["past", "evergreen"] };

        Expect(Judge(selector, Post("type:event", "domain:style-fashion")), SelectorVerdict.Match, "type:event", "domain:style-fashion");
    }

    [Fact]
    public void Evaluate_ShouldCountAnExcludedTimeframeAsATagClause()
    {
        var selector = new SubscriptionSelector { ExcludeTimeframes = ["past"] };

        Expect(Judge(selector, Post()), SelectorVerdict.Pending, "untagged");
        Expect(Judge(selector, Post("timeframe:past")), SelectorVerdict.NoMatch, "exclude-timeframe:past");
        var passing = Judge(selector, Post("domain:style-fashion", "timeframe:upcoming"));
        Expect(passing, SelectorVerdict.Match);
    }

    [Fact]
    public void Evaluate_ShouldNeedAnEventKind_ForTheEventSchema()
    {
        var news = Post(PostKind.News, [Small("type:event"), Small("domain:style-fashion")]);

        Expect(Judge(Fashion(), news, PostEnvelopeConstants.EventSchema), SelectorVerdict.NoMatch, "no-schema");
        Expect(Judge(Fashion(), news, schema: null), SelectorVerdict.Match, "type:event", "domain:style-fashion");
        Expect(Judge(Fashion(), Post(null, [Small("type:event")]), PostEnvelopeConstants.EventSchema), SelectorVerdict.NoMatch, "no-schema");
    }

    [Fact]
    public void Evaluate_ShouldLetExcludeWin()
    {
        var selector = Fashion() with
        {
            Sources = new SelectorSources { Include = [new() { GroupId = GroupId }], Exclude = [new() { SourceId = SourceId }] },
        };

        var result = Judge(selector, Post("type:event", "domain:style-fashion"));

        Expect(result, SelectorVerdict.NoMatch, "no-source");
        result.Context.Should().BeEmpty();
    }

    [Fact]
    public void Evaluate_ShouldRefuseALink_ThatNoIncludeNames()
    {
        var selector = Fashion() with { Sources = new SelectorSources { Include = [new() { GroupId = Guid.NewGuid() }] } };

        Expect(Judge(selector, Post("type:event", "domain:style-fashion")), SelectorVerdict.NoMatch, "no-source");
    }

    [Fact]
    public void Evaluate_ShouldMatchASectionRef_OnlyInsideItsPathScope()
    {
        var sections = new Dictionary<Guid, SelectorSection>
        {
            [PipelineId] = new(PipelineId, SourceId, new PipelineScope { Url = "https://example.test/events", RestrictToPath = true }),
        };
        var selector = Fashion() with { Sources = new SelectorSources { Include = [new() { PipelineId = PipelineId }] } };
        var inside = Post("type:event", "domain:style-fashion");
        var outside = inside with { Links = [new SelectorLink(LinkId, SourceId, GroupId, "https://example.test/news/1")] };

        Expect(Judge(selector, inside, sections: sections), SelectorVerdict.Match, "type:event", "domain:style-fashion");
        Expect(Judge(selector, outside, sections: sections), SelectorVerdict.NoMatch, "no-source");
        Expect(Judge(selector, inside, sections: NoSections), SelectorVerdict.NoMatch, "no-source");
    }

    [Fact]
    public void Evaluate_ShouldMergeRefContexts_GroupThenSourceThenSection()
    {
        var sections = new Dictionary<Guid, SelectorSection>
        {
            [PipelineId] = new(PipelineId, SourceId, new PipelineScope()),
        };
        var selector = Fashion() with
        {
            Sources = new SelectorSources
            {
                Include =
                [
                    new() { PipelineId = PipelineId, Context = Context(("d", "section")) },
                    new() { SourceId = SourceId, Context = Context(("c", "source"), ("d", "source"), ("e", "first")) },
                    new() { SourceId = SourceId, Context = Context(("e", "second")) },
                    new() { GroupId = GroupId, Context = Context(("b", "group"), ("c", "group"), ("d", "group")) },
                    new() { GroupId = Guid.NewGuid(), Context = Context(("z", "elsewhere")) },
                ],
            },
        };

        var result = Judge(selector, Post("type:event", "domain:style-fashion"), sections: sections,
            subscriptionContext: Context(("a", "subscription"), ("b", "subscription"), ("c", "subscription"), ("d", "subscription")));

        Expect(result, SelectorVerdict.Match, "type:event", "domain:style-fashion");
        result.Context.Should().BeEquivalentTo(new Dictionary<string, string>
        {
            ["a"] = "subscription", ["b"] = "group", ["c"] = "source", ["d"] = "section", ["e"] = "second",
        });
    }

    [Fact]
    public void Evaluate_ShouldTakeTheContextFromThePrimaryLink_WhenItIsEligible()
    {
        var selector = new SubscriptionSelector
        {
            Sources = new SelectorSources
            {
                Include =
                [
                    new() { SourceId = SourceId, Context = Context(("kind", "first")) },
                    new() { SourceId = OtherSourceId, Context = Context(("kind", "second")) },
                ],
                Exclude = [],
            },
        };
        var links = new[] { new SelectorLink(LinkId, SourceId, GroupId, "https://a.test/1"), new SelectorLink(OtherLinkId, OtherSourceId, null, "https://b.test/1") };
        var post = Post() with { Links = links };

        Judge(selector, post with { PrimaryLinkId = OtherLinkId }).Context["kind"].Should().Be("second");
        Judge(selector, post with { PrimaryLinkId = null }).Context["kind"].Should().Be("first");
        Judge(selector, post with { PrimaryLinkId = Guid.NewGuid() }).Context["kind"].Should().Be("first");
        var withoutSecond = selector with { Sources = selector.Sources with { Exclude = [new() { SourceId = OtherSourceId }] } };
        Judge(withoutSecond, post with { PrimaryLinkId = OtherLinkId }).Context["kind"].Should().Be("first");
    }

    [Fact]
    public void Evaluate_ShouldMatchOnSourcesAlone_WhenNoTagClause()
    {
        var selector = new SubscriptionSelector { Sources = new SelectorSources { Include = [new() { GroupId = GroupId, Context = Context(("defaultKind", "exhibition")) }] } };

        var result = Judge(selector, Post());

        Expect(result, SelectorVerdict.Match, "sources");
        result.Context["defaultKind"].Should().Be("exhibition");
    }

    [Fact]
    public void Choose_ShouldAppendPostNew_WhenUnseenAndMatching()
    {
        foreach (string type in ContentTypes)
            SubscriptionSelectorMatcher.Choose(type, SelectorVerdict.Match, seen: false, unmatched: false, stateChanged: false)
                .Should().Be(DeliveryEventTypes.PostNew, type);
    }

    [Theory]
    [InlineData(DeliveryEventTypes.LinkState)]
    [InlineData(DeliveryEventTypes.PostMerged)]
    [InlineData(DeliveryEventTypes.PostUnmerged)]
    [InlineData(DeliveryEventTypes.PostPurged)]
    [InlineData(DeliveryEventTypes.PostRelisted)]
    public void Choose_ShouldAppendNothing_WhenUnseenAndLifecycle(string type)
    {
        foreach (var verdict in Enum.GetValues<SelectorVerdict>())
            SubscriptionSelectorMatcher.Choose(type, verdict, seen: false, unmatched: false, stateChanged: true)
                .Should().BeNull($"{type} with {verdict}");
    }

    [Theory]
    [InlineData(SelectorVerdict.NoMatch)]
    [InlineData(SelectorVerdict.Pending)]
    public void Choose_ShouldAppendNothing_WhenUnseenAndNotMatching(SelectorVerdict verdict)
    {
        foreach (string type in ContentTypes.Concat(LifecycleTypes))
            SubscriptionSelectorMatcher.Choose(type, verdict, seen: false, unmatched: false, stateChanged: true)
                .Should().BeNull(type);
    }

    [Fact]
    public void Choose_ShouldAppendTheWritesOwnType_WhenSeenAndMatching()
    {
        foreach (string type in ContentTypes.Concat(LifecycleTypes))
            foreach (bool unmatched in new[] { false, true })
                SubscriptionSelectorMatcher.Choose(type, SelectorVerdict.Match, seen: true, unmatched, stateChanged: false)
                    .Should().Be(type, $"{type}, unmatched {unmatched}");
    }

    [Fact]
    public void Choose_ShouldAppendTheWritesOwnType_WhenSeenAndLifecycle()
    {
        foreach (string type in LifecycleTypes)
            foreach (var verdict in Enum.GetValues<SelectorVerdict>())
                foreach (bool unmatched in new[] { false, true })
                    SubscriptionSelectorMatcher.Choose(type, verdict, seen: true, unmatched, stateChanged: false)
                        .Should().Be(type, $"{type}, {verdict}, unmatched {unmatched}");
    }

    [Theory]
    [InlineData(SelectorVerdict.NoMatch)]
    [InlineData(SelectorVerdict.Pending)]
    public void Choose_ShouldAppendNothing_WhenTheNewestContentRecordIsUnmatched(SelectorVerdict verdict)
    {
        foreach (string type in ContentTypes)
            foreach (bool stateChanged in new[] { false, true })
                SubscriptionSelectorMatcher.Choose(type, verdict, seen: true, unmatched: true, stateChanged)
                    .Should().BeNull($"{type}, state changed {stateChanged}");
    }

    [Fact]
    public void Choose_ShouldAppendPostUnmatched_WhenAHeldPostStopsMatching()
    {
        foreach (string type in ContentTypes)
            SubscriptionSelectorMatcher.Choose(type, SelectorVerdict.NoMatch, seen: true, unmatched: false, stateChanged: false)
                .Should().Be(DeliveryEventTypes.PostUnmatched, type);
    }

    [Fact]
    public void Choose_ShouldAppendTheWritesType_OnlyWhenAPendingPostChangedState()
    {
        foreach (string type in ContentTypes)
        {
            SubscriptionSelectorMatcher.Choose(type, SelectorVerdict.Pending, seen: true, unmatched: false, stateChanged: true).Should().Be(type);
            SubscriptionSelectorMatcher.Choose(type, SelectorVerdict.Pending, seen: true, unmatched: false, stateChanged: false).Should().BeNull();
        }
    }

    private static void Expect(SelectorEvaluation result, SelectorVerdict verdict, params string[] clauses)
    {
        result.Verdict.Should().Be(verdict);
        result.Clauses.Should().Equal(clauses);
    }

    private static SelectorEvaluation Judge(SubscriptionSelector selector, SelectorPostFacts post, string? schema = null,
        IReadOnlyDictionary<Guid, SelectorSection>? sections = null, IReadOnlyDictionary<string, string>? subscriptionContext = null) =>
        SubscriptionSelectorMatcher.Evaluate(selector, post, sections ?? NoSections, schema, subscriptionContext ?? NoContext);

    /// <summary>Ocharo's shape: events under fashion without beauty, apparel industry, and clothing topics.</summary>
    private static SubscriptionSelector Fashion() => new()
    {
        EntityTypes = ["event"],
        Domains =
        [
            new SelectorDomain { Key = "style-fashion", Except = ["style-fashion/beauty"] },
            new SelectorDomain { Key = "business-and-finance/industries/apparel-industry" },
        ],
        Topics = ["textile", "clothing", "fashion-show", "fashion-week", "runway"],
    };

    private static SelectorPostFacts Post(params string[] tags) => Post(PostKind.Event, tags.Select(Small).ToArray());

    private static SelectorPostFacts Post(PostKind? kind, IReadOnlyList<PostTag> tags) => new()
    {
        Envelope = new PostEnvelope
        {
            Kind = kind, Title = "A post", Tags = tags, Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
        },
        Links = [new SelectorLink(LinkId, SourceId, GroupId, "https://example.test/events/1")],
        PrimaryLinkId = LinkId,
    };

    private static PostTag Small(string tag) => new() { Tag = tag, Tier = ExtractionTier.SmallModel };

    private static Dictionary<string, string> Context(params (string Key, string Value)[] pairs) =>
        pairs.ToDictionary(pair => pair.Key, pair => pair.Value);
}
