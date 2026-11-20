using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;

namespace HarvestAdapters.Tests.Unit.Ocharo;

/// <summary>Ocharo's one selector, <c>ocharo-selector.json</c>: the shipped file resolved against fake source groups and judged
/// by <see cref="SubscriptionSelectorMatcher"/>, so the clauses that decide each case are the ones a client's events carry.</summary>
public sealed class OcharoSelectorTests
{
    private static readonly Guid Container = Guid.Parse("00000000-0000-7000-8000-000000000001");

    /// <summary>The container's source groups by registry id: the four publishers the file lists and Hunarmand, which it does not.</summary>
    private static readonly IReadOnlyDictionary<string, SourceGroupModel> Groups = new[] { "afisha", "anons", "caex", "uzexpocentre", "hunarmand" }
        .Select((id, index) => (Id: id, Group: new SourceGroupModel
        {
            Id = Guid.Parse($"00000000-0000-7000-8000-0000000001{index:D2}"), ContainerId = Container,
            Name = OcharoSourceRegistry.Current.Groups.Single(group => group.Id == id).Name,
        }))
        .ToDictionary(row => row.Id, row => row.Group);

    /// <summary>The shipped file's clauses with its publishers listed, not followed: the explicit form, which most tests below judge.</summary>
    private static readonly OcharoSelector Explicit = OcharoSelector.Current with { FollowRegistry = false };

    private static readonly SubscriptionSelector Resolved = Explicit.Resolve([.. Groups.Values]).ValueOrThrow();

    /// <summary>The shipped file as it ships, following the registry, resolved over the same five groups.</summary>
    private static readonly SubscriptionSelector Followed = OcharoSelector.Current.Resolve([.. Groups.Values]).ValueOrThrow();

    [Fact]
    public void File_ShouldDeclareTheEventsSubscriptionFollowingTheRegistry_WithTheFourPublishersAsOverrides()
    {
        var file = OcharoSelector.Current;

        file.Format.Should().Be("ocharo-selector@1");
        file.Name.Should().Be("ocharo-events");
        file.Schema.Should().Be("wow2.event.v1");
        file.Context.Should().Equal(new Dictionary<string, string> { ["catalog"] = "ocharo-fashion" });
        file.FollowRegistry.Should().BeTrue();
        file.Sources.Select(source => source.Group).Should().Equal("afisha", "anons", "caex", "uzexpocentre");
        file.Sources.Where(source => source.Context.Count > 0).Select(source => source.Group).Should().Equal("caex", "uzexpocentre");
        SelectorRules.IsName(file.Name).Should().BeTrue();
    }

    [Fact]
    public void Resolve_ShouldBindEachPublisherToItsGroup_AndAddTheRegistryNames()
    {
        var include = Resolved.Sources.Include;

        include.Select(reference => reference.GroupId).Should().Equal(Groups["afisha"].Id, Groups["anons"].Id, Groups["caex"].Id, Groups["uzexpocentre"].Id);
        include.Should().OnlyContain(reference => !reference.SourceId.HasValue && !reference.PipelineId.HasValue);
        Resolved.Sources.Exclude.Should().BeEmpty();
        include[0].Context.Should().Equal(new Dictionary<string, string> { ["sourceGroup"] = "afisha", ["sourceName"] = "Afisha Uzbekistan" },
            "Afisha keeps no defaultKind: its two pipelines share one source");
        include[2].Context.Should().Equal(new Dictionary<string, string>
            { ["defaultKind"] = "exhibition", ["sourceGroup"] = "caex", ["sourceName"] = "CAEx Uzbekistan" });
        include[3].Context.Should().Equal(new Dictionary<string, string>
            { ["defaultKind"] = "trade-fair", ["sourceGroup"] = "uzexpocentre", ["sourceName"] = "Uzexpocentre" });
    }

    [Fact]
    public void Resolve_ShouldMatchAGroupByTheRegistrysNameWithoutRegardToCase()
    {
        var shouted = Groups.Values.Select(group => group with { Name = group.Name.ToUpperInvariant() }).ToList();

        Explicit.Resolve(shouted).Should().BeOfType<Result<SubscriptionSelector>.Success>();
        OcharoSelector.Current.Resolve(shouted).ValueOrThrow().Sources.Include.Should().HaveCount(5);
    }

    [Fact]
    public void Resolve_ShouldRefuseAPublisherTheContainerLacks()
    {
        var result = Explicit.Resolve([.. Groups.Values.Where(group => group.Id != Groups["caex"].Id)]);

        var failure = result.Should().BeOfType<Result<SubscriptionSelector>.Failure>().Subject;
        failure.Error.Message.Should().Be("Group caex is not provisioned in the Ocharo container; run setup with --group caex.");
    }

    [Fact]
    public void Resolve_WithoutFollowRegistry_ShouldLeaveOutAGroupTheFileDoesNotList_AndRefuseAnEmptyContainer()
    {
        Resolved.Sources.Include.Select(reference => reference.GroupId).Should()
            .NotContain(Groups["hunarmand"].Id, "the container holds Hunarmand but the explicit file does not list it");

        var failure = Explicit.Resolve([]).Should().BeOfType<Result<SubscriptionSelector>.Failure>().Subject;
        failure.Error.Message.Should().Be("Group afisha is not provisioned in the Ocharo container; run setup with --group afisha.");
    }

    [Fact]
    public void Resolve_WithFollowRegistry_ShouldBindEveryProvisionedGroup_InRegistryRankOrder_KeepingTheOverrides()
    {
        // Out of rank order, with Teplo (rank 2) that the file does not list and a group the registry does not know.
        var stranger = new SourceGroupModel { Id = Guid.Parse("00000000-0000-7000-8000-000000000999"), ContainerId = Container, Name = "Not a publisher" };
        var container = new[] { "uzexpocentre", "teplo", "caex", "afisha", "hunarmand" }.Select(GroupOf).Append(stranger).ToList();

        var resolved = OcharoSelector.Current.Resolve(container).ValueOrThrow();

        var include = resolved.Sources.Include;
        include.Select(reference => reference.Context["sourceGroup"]).Should().Equal("hunarmand", "teplo", "afisha", "caex", "uzexpocentre");
        include.Select(reference => reference.GroupId).Should().Equal(
            container[4].Id, container[1].Id, container[3].Id, container[2].Id, container[0].Id);
        include.Should().OnlyContain(reference => !reference.SourceId.HasValue && !reference.PipelineId.HasValue);
        include[0].Context.Should().Equal(new Dictionary<string, string>
            { ["sourceGroup"] = "hunarmand", ["sourceName"] = "Hunarmand and Craftfair" }, "a followed publisher with no entry adds no defaults");
        include[1].Context.Should().Equal(new Dictionary<string, string>
            { ["sourceGroup"] = "teplo", ["sourceName"] = "Teplo Store" });
        include[2].Context.Should().Equal(new Dictionary<string, string>
            { ["sourceGroup"] = "afisha", ["sourceName"] = "Afisha Uzbekistan" });
        include[3].Context.Should().Equal(new Dictionary<string, string>
            { ["defaultKind"] = "exhibition", ["sourceGroup"] = "caex", ["sourceName"] = "CAEx Uzbekistan" });
        include[4].Context.Should().Equal(new Dictionary<string, string>
            { ["defaultKind"] = "trade-fair", ["sourceGroup"] = "uzexpocentre", ["sourceName"] = "Uzexpocentre" });
        resolved.Sources.Exclude.Should().BeEmpty();
        (resolved with { Sources = Resolved.Sources }).Should().BeEquivalentTo(Resolved, "only the sources differ from the explicit form");
        SelectorRules.Validate(resolved).Should().BeNull();
    }

    [Fact]
    public void Resolve_WithFollowRegistry_ShouldSkipAListedPublisherTheContainerLacks()
    {
        var resolved = OcharoSelector.Current.Resolve([Groups["afisha"], Groups["anons"]]).ValueOrThrow();

        resolved.Sources.Include.Select(reference => reference.Context["sourceGroup"]).Should().Equal("afisha", "anons");
    }

    [Fact]
    public void Resolve_WithFollowRegistry_ShouldNeedNoListedPublisher()
    {
        var bare = OcharoSelector.Current with { Sources = [] };

        var resolved = bare.Resolve([Groups["caex"], Groups["afisha"]]).ValueOrThrow();

        resolved.Sources.Include.Select(reference => reference.Context["sourceGroup"]).Should().Equal("afisha", "caex");
        resolved.Sources.Include.Should().OnlyContain(reference => !reference.Context.ContainsKey("defaultKind"));
    }

    [Fact]
    public void Resolve_WithFollowRegistry_ShouldRefuseAContainerWithNoRegistryPublisher()
    {
        var stranger = new SourceGroupModel { Id = Guid.Parse("00000000-0000-7000-8000-000000000999"), ContainerId = Container, Name = "Not a publisher" };

        foreach (IReadOnlyList<SourceGroupModel> container in new IReadOnlyList<SourceGroupModel>[] { [], [stranger] })
        {
            var failure = OcharoSelector.Current.Resolve(container).Should().BeOfType<Result<SubscriptionSelector>.Failure>().Subject;
            failure.Error.Message.Should().Be("No registry publisher is provisioned in the Ocharo container; run setup.");
        }
    }

    [Fact]
    public void Validate_ShouldAllowNoListedPublisher_OnlyWhenTheSelectorFollowsTheRegistry()
    {
        var bare = OcharoSelector.Current with { Sources = [] };

        bare.Invoking(file => file.Validate()).Should().NotThrow();
        (bare with { FollowRegistry = false }).Invoking(file => file.Validate()).Should().Throw<InvalidOperationException>();
        Explicit.Invoking(file => file.Validate()).Should().NotThrow();
        (OcharoSelector.Current with { Sources = [new() { Group = "afisha" }, new() { Group = "afisha" }] })
            .Invoking(file => file.Validate()).Should().Throw<InvalidOperationException>("a publisher is listed once");
        (OcharoSelector.Current with { Sources = [new() { Group = "not-in-the-registry" }] })
            .Invoking(file => file.Validate()).Should().Throw<InvalidOperationException>("an override names a registry publisher");
    }

    [Fact]
    public void Selector_ShouldBeValid_AndStayAsItIsWhenNormalized()
    {
        SelectorRules.Validate(Resolved).Should().BeNull();
        SelectorRules.Normalize(Resolved).Should().BeEquivalentTo(Resolved, options => options.WithStrictOrdering());
        Resolved.EntityTypes.Should().Equal("event");
        Resolved.Domains.Select(domain => domain.Key).Should().Equal("style-fashion", "business-and-finance/industries/apparel-industry");
        Resolved.Domains[0].Except.Should().Equal("style-fashion/beauty", "style-fashion/personal-care", "style-fashion/body-art");
        Resolved.ExcludeTimeframes.Should().Equal("past");
        Resolved.Topics.Should().Contain(["clothing", "footwear", "fashion-show", "fashion-week", "runway", "textile"])
            .And.NotContain(["jewelry", "cosmetics"]);
    }

    [Fact]
    public void VisaFashionWeek_ShouldMatch()
    {
        var verdict = Judge("afisha", "event", ["style-fashion/high-fashion"], ["runway", "fashion-week"]);

        verdict.Verdict.Should().Be(SelectorVerdict.Match);
        verdict.Clauses.Should().Equal("type:event", "domain:style-fashion", "topic:fashion-week", "topic:runway");
    }

    [Fact]
    public void Texworld_ShouldMatch()
    {
        var verdict = Judge("uzexpocentre", "event", ["business-and-finance/industries/apparel-industry"], ["textile"]);

        verdict.Verdict.Should().Be(SelectorVerdict.Match);
        verdict.Clauses.Should().Equal("type:event", "domain:business-and-finance/industries/apparel-industry", "topic:textile");
        verdict.Context.Should().Contain("defaultKind", "trade-fair").And.Contain("catalog", "ocharo-fashion");
    }

    [Fact]
    public void TftExpo_ShouldMatch()
    {
        var verdict = Judge("anons", "event", ["business-and-finance/industries/apparel-industry", "style-fashion/fashion-trends"], ["fashion-expo", "textile"]);

        verdict.Verdict.Should().Be(SelectorVerdict.Match);
        verdict.Clauses.Should().Equal("type:event", "domain:style-fashion", "domain:business-and-finance/industries/apparel-industry", "topic:textile");
    }

    [Fact]
    public void CaexBuildAndCo_ShouldNotMatch()
    {
        var verdict = Judge("caex", "event", ["business-and-finance/industries/construction-industry"], ["construction"]);

        verdict.Verdict.Should().Be(SelectorVerdict.NoMatch);
        verdict.Clauses.Should().Equal("no-subject");
    }

    [Fact]
    public void Marathon_ShouldNotMatch()
    {
        var verdict = Judge("afisha", "event", ["healthy-living/fitness-and-exercise/running-and-jogging"], ["marathon"]);

        verdict.Verdict.Should().Be(SelectorVerdict.NoMatch);
        verdict.Clauses.Should().Equal("no-subject");
    }

    [Fact]
    public void BeautyExpoWithoutClothing_ShouldNotMatch()
    {
        var verdict = Judge("uzexpocentre", "event", ["style-fashion/beauty/makeup-and-accessories"], ["cosmetics"]);

        verdict.Verdict.Should().Be(SelectorVerdict.NoMatch);
        verdict.Clauses.Should().Equal("except:style-fashion/beauty");
    }

    [Theory]
    [InlineData("style-fashion/personal-care/shaving", "except:style-fashion/personal-care")]
    [InlineData("style-fashion/body-art", "except:style-fashion/body-art")]
    public void PersonalCareAndBodyArtWithoutClothing_ShouldNotMatch(string node, string clause)
    {
        var verdict = Judge("afisha", "event", [node], ["tattoo"]);

        verdict.Verdict.Should().Be(SelectorVerdict.NoMatch);
        verdict.Clauses.Should().Equal(clause);
    }

    [Fact]
    public void BeautyExpoWithAFashionShow_ShouldMatch()
    {
        var verdict = Judge("uzexpocentre", "event", ["style-fashion/beauty/makeup-and-accessories"], ["cosmetics", "fashion-show"]);

        verdict.Verdict.Should().Be(SelectorVerdict.Match);
        verdict.Clauses.Should().Equal("type:event", "topic:fashion-show");
    }

    [Fact]
    public void DesignerClothingOnAnArtDomain_ShouldMatch()
    {
        var verdict = Judge("afisha", "event", ["fine-art/design"], ["designer-clothing"]);

        verdict.Verdict.Should().Be(SelectorVerdict.Match);
        verdict.Clauses.Should().Equal("type:event", "topic:clothing");
    }

    [Fact]
    public void FashionListing_ShouldNotMatch()
    {
        var verdict = Judge("afisha", "listing", ["style-fashion/womens-fashion/womens-clothing"], ["clothing"]);

        verdict.Verdict.Should().Be(SelectorVerdict.NoMatch);
        verdict.Clauses.Should().Equal("no-type:listing");
    }

    [Fact]
    public void UnsureFashionPost_ShouldBePending()
    {
        var verdict = Judge("afisha", null, ["style-fashion/high-fashion"], ["runway"]);

        verdict.Verdict.Should().Be(SelectorVerdict.Pending);
        verdict.Clauses.Should().Equal("unsure-type");
    }

    [Fact]
    public void CraftfairPost_ShouldNotMatch()
    {
        var verdict = Judge("hunarmand", "event", ["style-fashion/high-fashion"], ["runway"]);

        verdict.Verdict.Should().Be(SelectorVerdict.NoMatch);
        verdict.Clauses.Should().Equal("no-source");
    }

    [Fact]
    public void CraftfairPost_ShouldMatchWhenTheSelectorFollowsTheRegistry_WhichBindsEveryProvisionedGroup()
    {
        var post = Post("hunarmand", PostKind.Event, Tags("event", ["style-fashion/high-fashion"], ["runway"], null));

        var verdict = Evaluate(post, Followed);

        verdict.Verdict.Should().Be(SelectorVerdict.Match);
        verdict.Context.Should().Contain("sourceGroup", "hunarmand").And.Contain("catalog", "ocharo-fashion");
    }

    [Fact]
    public void UntaggedPost_ShouldBePending()
    {
        var verdict = Evaluate(Post("afisha", PostKind.Event, []));

        verdict.Verdict.Should().Be(SelectorVerdict.Pending);
        verdict.Clauses.Should().Equal("untagged");
    }

    [Fact]
    public void NonEventPost_ShouldNotMatchTheEventSchema()
    {
        var verdict = Evaluate(Post("afisha", PostKind.News, Tags("event", ["style-fashion/high-fashion"], ["runway"], null)));

        verdict.Verdict.Should().Be(SelectorVerdict.NoMatch);
        verdict.Clauses.Should().Equal("no-schema");
    }

    [Fact]
    public void PastEventReport_ShouldNotMatch_WhileOtherTimeframesAndNoTimeframeDo()
    {
        var past = Judge("afisha", "event", ["style-fashion/high-fashion"], ["runway"], "past");
        past.Verdict.Should().Be(SelectorVerdict.NoMatch);
        past.Clauses.Should().Equal("exclude-timeframe:past");

        foreach (string timeframe in new[] { "upcoming", "ongoing", "evergreen", "unsure" })
            Judge("afisha", "event", ["style-fashion/high-fashion"], ["runway"], timeframe).Verdict.Should().Be(SelectorVerdict.Match, timeframe);
        Judge("afisha", "event", ["style-fashion/high-fashion"], ["runway"], null).Verdict.Should().Be(SelectorVerdict.Match, "no timeframe tag: no effect");
    }

    [Fact]
    public void Context_ShouldFollowThePublisherOfTheLink()
    {
        Judge("afisha", "event", ["style-fashion/high-fashion"], []).Context.Should().Equal(new Dictionary<string, string>
            { ["catalog"] = "ocharo-fashion", ["sourceGroup"] = "afisha", ["sourceName"] = "Afisha Uzbekistan" }, "Afisha names no defaultKind");
        Judge("caex", "event", ["style-fashion/high-fashion"], []).Context.Should().Equal(new Dictionary<string, string>
            { ["catalog"] = "ocharo-fashion", ["defaultKind"] = "exhibition", ["sourceGroup"] = "caex", ["sourceName"] = "CAEx Uzbekistan" });
    }

    private static SelectorEvaluation Judge(string group, string? type, string[] domains, string[] topics, string? timeframe = null) =>
        Evaluate(Post(group, PostKind.Event, Tags(type, domains, topics, timeframe)));

    private static SelectorEvaluation Evaluate(SelectorPostFacts post, SubscriptionSelector? selector = null) =>
        SubscriptionSelectorMatcher.Evaluate(selector ?? Resolved, post,
            new Dictionary<Guid, SelectorSection>(), OcharoSelector.Current.Schema, OcharoSelector.Current.Context);

    /// <summary>The container's group for a registry publisher, named as the registry names it.</summary>
    private static SourceGroupModel GroupOf(string id)
    {
        var publisher = OcharoSourceRegistry.Current.Groups.Single(group => group.Id == id);
        return new SourceGroupModel { Id = Guid.Parse($"00000000-0000-7000-8000-000000000{publisher.Rank:D3}"), ContainerId = Container, Name = publisher.Name };
    }

    private static SelectorPostFacts Post(string group, PostKind kind, IReadOnlyList<PostTag> tags)
    {
        var link = new SelectorLink(Guid.Parse("00000000-0000-7000-8000-000000000201"), Guid.Parse("00000000-0000-7000-8000-000000000301"),
            Groups[group].Id, "https://source.example.test/events/1");
        return new SelectorPostFacts
        {
            Envelope = new PostEnvelope
            {
                Kind = kind, Title = "A post", Tags = tags,
                Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
            },
            Links = [link], PrimaryLinkId = link.LinkId,
        };
    }

    /// <summary>Adds the model-tier tags the post-tags overlay writes: the type, each domain with every ancestor, the topics and
    /// the timeframe. A node and its ancestors both ride the post, so the selector must read the most specific.</summary>
    private static IReadOnlyList<PostTag> Tags(string? type, string[] domains, string[] topics, string? timeframe)
    {
        var tags = new List<string>();
        if (type is not null)
            tags.Add("type:" + type);
        foreach (string domain in domains)
        {
            var parts = domain.Split('/');
            for (int length = 1; length <= parts.Length; length++)
                tags.Add("domain:" + string.Join('/', parts.Take(length)));
        }
        tags.AddRange(topics.Select(topic => "topic:" + topic));
        if (timeframe is not null)
            tags.Add("timeframe:" + timeframe);
        return [.. tags.Distinct().Select(tag => new PostTag { Tag = tag, Tier = ExtractionTier.SmallModel, Version = "post-tags@3" })];
    }
}
