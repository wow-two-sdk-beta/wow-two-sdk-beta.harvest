using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

namespace Harvest.Tests.Unit.Delivery;

public sealed class SelectorRulesTests
{
    private static readonly Guid GroupId = Guid.Parse("0f000000-0000-0000-0000-000000000001");
    private static readonly Guid SourceId = Guid.Parse("0f000000-0000-0000-0000-000000000002");

    public static TheoryData<string, SubscriptionSelector, string> Refusals => new()
    {
        { "unknown entity type", new() { EntityTypes = ["article"] }, "Entity type" },
        { "uppercase domain key", new() { Domains = [new() { Key = "Style-Fashion" }] }, "taxonomy key" },
        { "domain key with a space", new() { Domains = [new() { Key = "style fashion" }] }, "taxonomy key" },
        { "domain key with a trailing slash", new() { Domains = [new() { Key = "style-fashion/" }] }, "taxonomy key" },
        { "empty domain key", new() { Domains = [new() { Key = "" }] }, "taxonomy key" },
        { "bad excluded domain key", new() { ExcludeDomains = ["sports//running"] }, "Excluded domain key" },
        { "except equal to its key", new() { Domains = [new() { Key = "style-fashion", Except = ["style-fashion"] }] }, "strictly under" },
        { "except beside its key", new() { Domains = [new() { Key = "style-fashion", Except = ["style-fashionable/beauty"] }] }, "strictly under" },
        { "except of another domain", new() { Domains = [new() { Key = "style-fashion", Except = ["sports/running"] }] }, "strictly under" },
        { "except above its key", new() { Domains = [new() { Key = "style-fashion/beauty", Except = ["style-fashion"] }] }, "strictly under" },
        { "bad except key", new() { Domains = [new() { Key = "style-fashion", Except = ["style-fashion/Beauty"] }] }, "taxonomy key" },
        { "uppercase topic", new() { Topics = ["Fashion-Show"] }, "Topic" },
        { "topic with a space", new() { Topics = ["fashion show"] }, "Topic" },
        { "topic with a leading hyphen", new() { Topics = ["-runway"] }, "Topic" },
        { "topic over 40 characters", new() { Topics = [new string('a', 41)] }, "Topic" },
        { "bad excluded topic", new() { ExcludeTopics = ["city/marathon"] }, "Topic" },
        { "unknown timeframe", new() { ExcludeTimeframes = ["soon"] }, "Timeframe" },
        { "uppercase timeframe", new() { ExcludeTimeframes = ["Past"] }, "Timeframe" },
        { "ref without an id", new() { Sources = new() { Include = [new()] } }, "exactly one" },
        { "ref with two ids", new() { Sources = new() { Include = [new() { GroupId = GroupId, SourceId = SourceId }] } }, "exactly one" },
        { "ref with three ids", new() { Sources = new() { Exclude = [new() { GroupId = GroupId, SourceId = SourceId, PipelineId = Guid.NewGuid() }] } }, "exactly one" },
        { "exclude ref with context", new() { Sources = new() { Exclude = [new() { SourceId = SourceId, Context = Pairs(1) }] } }, "excluded source ref" },
        { "domains over the limit", new() { Domains = Enumerable.Range(0, SelectorRules.MaxDomains + 1).Select(index => new SelectorDomain { Key = "domain-" + index }).ToArray() }, "at most 50 domains" },
        { "exceptions over the limit", new() { Domains = [new() { Key = "style-fashion", Except = Enumerable.Range(0, SelectorRules.MaxExcept + 1).Select(index => "style-fashion/node-" + index).ToArray() }] }, "at most 20 exceptions" },
        { "topics over the limit", new() { Topics = Enumerable.Range(0, SelectorRules.MaxTopics + 1).Select(index => "topic-" + index).ToArray() }, "at most 100 topics" },
        { "excluded topics over the limit", new() { ExcludeTopics = Enumerable.Range(0, SelectorRules.MaxTopics + 1).Select(index => "topic-" + index).ToArray() }, "excludes at most 100 topics" },
        { "excluded domains over the limit", new() { ExcludeDomains = Enumerable.Range(0, SelectorRules.MaxExcludeDomains + 1).Select(index => "domain-" + index).ToArray() }, "excludes at most 50 domains" },
        { "excluded timeframes over the limit", new() { ExcludeTimeframes = ["upcoming", "ongoing", "past", "evergreen", "unsure", "upcoming"] }, "excludes at most 5 timeframes" },
        { "includes over the limit", new() { Sources = new() { Include = Refs(SelectorRules.MaxRefs + 1) } }, "include list" },
        { "excludes over the limit", new() { Sources = new() { Exclude = Refs(SelectorRules.MaxRefs + 1) } }, "exclude list" },
        { "context pairs over the limit", new() { Sources = new() { Include = [new() { SourceId = SourceId, Context = Pairs(SelectorRules.MaxRefContext + 1) }] } }, "at most 32 pairs" },
        { "context key over the limit", new() { Sources = new() { Include = [new() { SourceId = SourceId, Context = new Dictionary<string, string> { [new string('k', 65)] = "v" } }] } }, "Context key" },
        { "empty context key", new() { Sources = new() { Include = [new() { SourceId = SourceId, Context = new Dictionary<string, string> { [" "] = "v" } }] } }, "Context key" },
        { "context value over the limit", new() { Sources = new() { Include = [new() { SourceId = SourceId, Context = new Dictionary<string, string> { ["k"] = new string('v', 513) } }] } }, "at most 512" },
    };

    [Theory]
    [MemberData(nameof(Refusals))]
    public void Validate_ShouldRefuse(string reason, SubscriptionSelector selector, string expected)
    {
        var problem = SelectorRules.Validate(selector);

        problem.Should().NotBeNull(reason);
        problem.Should().Contain(expected, because: reason);
    }

    [Fact]
    public void Validate_ShouldAccept_AnEmptySelector_AndOneAtEveryLimit()
    {
        SelectorRules.Validate(new SubscriptionSelector()).Should().BeNull();

        var atTheLimits = new SubscriptionSelector
        {
            EntityTypes = ["event", "listing"],
            Domains = Enumerable.Range(0, SelectorRules.MaxDomains).Select(index => new SelectorDomain
            {
                Key = "domain-" + index, Except = Enumerable.Range(0, SelectorRules.MaxExcept).Select(node => $"domain-{index}/node-{node}").ToArray(),
            }).ToArray(),
            Topics = Enumerable.Range(0, SelectorRules.MaxTopics).Select(index => "topic-" + index).ToArray(),
            ExcludeDomains = Enumerable.Range(0, SelectorRules.MaxExcludeDomains).Select(index => "excluded/domain-" + index).ToArray(),
            ExcludeTopics = [new string('a', SelectorRules.MaxTopicLength)],
            ExcludeTimeframes = [.. SelectorRules.Timeframes],
            Sources = new SelectorSources
            {
                Include = [.. Refs(SelectorRules.MaxRefs - 1), new() { SourceId = SourceId, Context = Pairs(SelectorRules.MaxRefContext) }],
                Exclude = Refs(SelectorRules.MaxRefs),
            },
        };

        SelectorRules.Validate(atTheLimits).Should().BeNull();
    }

    [Theory]
    [InlineData("upcoming")]
    [InlineData("ongoing")]
    [InlineData("past")]
    [InlineData("evergreen")]
    [InlineData("unsure")]
    public void Validate_ShouldAcceptEachTimeframe(string timeframe)
    {
        SelectorRules.Validate(new SubscriptionSelector { ExcludeTimeframes = [timeframe] }).Should().BeNull();
    }

    [Fact]
    public void Validate_ShouldAcceptOcharosSelector()
    {
        var selector = new SubscriptionSelector
        {
            EntityTypes = ["event"],
            Domains =
            [
                new SelectorDomain { Key = "style-fashion", Except = ["style-fashion/beauty", "style-fashion/personal-care", "style-fashion/body-art"] },
                new SelectorDomain { Key = "business-and-finance/industries/apparel-industry" },
            ],
            Topics = ["textile", "textiles", "ikat", "fashion-show", "fashion-week", "runway"],
            ExcludeTimeframes = ["past"],
            Sources = new SelectorSources { Include = [new() { GroupId = GroupId, Context = new Dictionary<string, string> { ["defaultKind"] = "trade-fair" } }] },
        };

        SelectorRules.Validate(selector).Should().BeNull();
    }

    [Fact]
    public void Normalize_ShouldTrimLowercaseAndDedupe()
    {
        var raw = new SubscriptionSelector
        {
            EntityTypes = [" Event ", "EVENT", "listing"],
            Domains =
            [
                new SelectorDomain { Key = " Style-Fashion ", Except = [" Style-Fashion/Beauty", "style-fashion/beauty", "style-fashion/body-art"] },
                new SelectorDomain { Key = "style-fashion", Except = [] },
                new SelectorDomain { Key = "BUSINESS-and-finance/industries/apparel-industry" },
            ],
            Topics = [" Textile ", "textile", "Runway", "ikat"],
            ExcludeDomains = [" Sports ", "sports"],
            ExcludeTopics = ["Marathon", " marathon"],
            ExcludeTimeframes = [" Past ", "PAST", "upcoming", "Past"],
            Sources = new SelectorSources
            {
                Include =
                [
                    new() { GroupId = GroupId, Context = Pairs(2) },
                    new() { GroupId = GroupId, Context = Pairs(2) },
                    new() { GroupId = GroupId, Context = Pairs(3) },
                ],
                Exclude = [new() { SourceId = SourceId }, new() { SourceId = SourceId }],
            },
        };

        SelectorRules.Validate(raw).Should().NotBeNull();
        var normalized = SelectorRules.Normalize(raw);

        normalized.EntityTypes.Should().Equal("event", "listing");
        normalized.Domains.Select(domain => domain.Key).Should().Equal("style-fashion", "business-and-finance/industries/apparel-industry");
        normalized.Domains[0].Except.Should().Equal("style-fashion/beauty", "style-fashion/body-art");
        normalized.Topics.Should().Equal("textile", "runway", "ikat");
        normalized.ExcludeDomains.Should().Equal("sports");
        normalized.ExcludeTopics.Should().Equal("marathon");
        normalized.ExcludeTimeframes.Should().Equal("past", "upcoming");
        normalized.Sources.Include.Should().HaveCount(2);
        normalized.Sources.Include[0].Context.Should().BeEquivalentTo(Pairs(2));
        normalized.Sources.Include[1].Context.Should().BeEquivalentTo(Pairs(3));
        normalized.Sources.Exclude.Should().ContainSingle();
        SelectorRules.Validate(normalized).Should().BeNull();
        SelectorRules.Normalize(normalized).Should().BeEquivalentTo(normalized);
    }

    [Fact]
    public void Normalize_ShouldKeepContextAsGiven_AndReplaceNullsWithEmptyLists()
    {
        var raw = JsonSerializer.Deserialize<SubscriptionSelector>("""
            { "topics": null, "excludeTimeframes": null, "domains": [null], "sources": { "include": [ { "sourceId": "0f000000-0000-0000-0000-000000000002", "context": { "defaultKind": "Trade-Fair" } }, null ], "exclude": null } }
            """, JsonOptionsConstants.Default)!;

        var normalized = SelectorRules.Normalize(raw);

        normalized.Topics.Should().BeEmpty();
        normalized.ExcludeTimeframes.Should().BeEmpty();
        normalized.Domains.Should().BeEmpty();
        normalized.Sources.Include.Should().ContainSingle().Which.Context["defaultKind"].Should().Be("Trade-Fair");
        normalized.Sources.Exclude.Should().BeEmpty();
        SelectorRules.Validate(normalized).Should().BeNull();
    }

    [Fact]
    public void Pairs_ShouldCrossTypesWithDomainKeys()
    {
        var domains = new[] { new SelectorDomain { Key = "style-fashion", Except = ["style-fashion/beauty"] }, new SelectorDomain { Key = "sports" } };

        SelectorRules.Pairs(new SubscriptionSelector { Domains = domains }).Should().Equal(
            ("event", "style-fashion"), ("event", "sports"), ("listing", "style-fashion"), ("listing", "sports"));
        SelectorRules.Pairs(new SubscriptionSelector { EntityTypes = ["listing"], Domains = domains }).Should().Equal(("listing", "style-fashion"), ("listing", "sports"));
        SelectorRules.Pairs(new SubscriptionSelector { EntityTypes = ["event"], Topics = ["textile"], ExcludeDomains = ["sports"], ExcludeTopics = ["marathon"], ExcludeTimeframes = ["past"] })
            .Should().BeEmpty();
    }

    [Fact]
    public void MostSpecific_ShouldDropAncestors()
    {
        SelectorRules.MostSpecific(["style-fashion", "style-fashion/beauty", "style-fashion/beauty/makeup", "sports", "sports/running", "sports", "style", "style-fashion/womens-fashion"])
            .Should().Equal("style-fashion/beauty/makeup", "sports/running", "style", "style-fashion/womens-fashion");
        SelectorRules.MostSpecific([]).Should().BeEmpty();
    }

    [Theory]
    [InlineData("style-fashion", "style-fashion", true)]
    [InlineData("style-fashion/beauty", "style-fashion", true)]
    [InlineData("style-fashion/beauty/makeup", "style-fashion/beauty", true)]
    [InlineData("style-fashionable", "style-fashion", false)]
    [InlineData("style", "style-fashion", false)]
    [InlineData("style-fashion", "style-fashion/beauty", false)]
    public void AtOrUnder_ShouldRespectPathBoundaries(string node, string key, bool expected)
    {
        SelectorRules.AtOrUnder(node, key).Should().Be(expected);
    }

    [Theory]
    [InlineData("ocharo-events", true)]
    [InlineData("a", true)]
    [InlineData("0-a", true)]
    [InlineData("", false)]
    [InlineData("-events", false)]
    [InlineData("Ocharo", false)]
    [InlineData("ocharo events", false)]
    [InlineData("ocharo-events\n", false)]
    [InlineData(null, false)]
    public void IsName_ShouldAcceptLowercaseSlugs(string? name, bool expected)
    {
        SelectorRules.IsName(name).Should().Be(expected);
    }

    [Fact]
    public void IsName_ShouldLimitTheLengthTo64()
    {
        SelectorRules.IsName(new string('a', 64)).Should().BeTrue();
        SelectorRules.IsName(new string('a', 65)).Should().BeFalse();
    }

    [Fact]
    public void HasTagClause_ShouldBeTrue_ForEachTagFieldAndFalseForSourcesAlone()
    {
        SelectorRules.HasTagClause(new SubscriptionSelector()).Should().BeFalse();
        SelectorRules.HasTagClause(new SubscriptionSelector { Sources = new SelectorSources { Include = [new() { GroupId = GroupId }] } }).Should().BeFalse();
        SelectorRules.HasTagClause(new SubscriptionSelector { EntityTypes = ["event"] }).Should().BeTrue();
        SelectorRules.HasTagClause(new SubscriptionSelector { Domains = [new() { Key = "sports" }] }).Should().BeTrue();
        SelectorRules.HasTagClause(new SubscriptionSelector { Topics = ["runway"] }).Should().BeTrue();
        SelectorRules.HasTagClause(new SubscriptionSelector { ExcludeDomains = ["sports"] }).Should().BeTrue();
        SelectorRules.HasTagClause(new SubscriptionSelector { ExcludeTopics = ["marathon"] }).Should().BeTrue();
        SelectorRules.HasTagClause(new SubscriptionSelector { ExcludeTimeframes = ["past"] }).Should().BeTrue();
    }

    [Fact]
    public void Selector_ShouldRoundTripOnTheWire()
    {
        var selector = new SubscriptionSelector
        {
            EntityTypes = ["event"],
            Domains = [new SelectorDomain { Key = "style-fashion", Except = ["style-fashion/beauty"] }, new SelectorDomain { Key = "business-and-finance/industries/apparel-industry" }],
            Topics = ["textile", "ikat"],
            ExcludeDomains = ["sports"],
            ExcludeTopics = ["marathon"],
            ExcludeTimeframes = ["past", "evergreen"],
            Sources = new SelectorSources
            {
                Include = [new() { GroupId = GroupId, Context = new Dictionary<string, string> { ["defaultKind"] = "trade-fair" } }],
                Exclude = [new() { SourceId = SourceId }],
            },
        };

        string json = JsonSerializer.Serialize(selector, JsonOptionsConstants.Default);
        var wire = JsonNode.Parse(json)!.AsObject();

        wire["excludeTimeframes"]!.AsArray().Select(node => node!.GetValue<string>()).Should().Equal("past", "evergreen");
        wire["entityTypes"]!.AsArray().Should().ContainSingle();
        wire["domains"]![0]!["except"]![0]!.GetValue<string>().Should().Be("style-fashion/beauty");
        wire["sources"]!["include"]![0]!["groupId"]!.GetValue<string>().Should().Be(GroupId.ToString());
        wire["sources"]!["include"]![0]!["context"]!["defaultKind"]!.GetValue<string>().Should().Be("trade-fair");
        wire["sources"]!["include"]![0]!.AsObject().ContainsKey("sourceId").Should().BeFalse("nulls are omitted");
        var back = JsonSerializer.Deserialize<SubscriptionSelector>(json, JsonOptionsConstants.Default)!;
        back.Should().BeEquivalentTo(selector);
        back.ExcludeTimeframes.Should().Equal("past", "evergreen");
        SelectorRules.Validate(back).Should().BeNull();
    }

    [Fact]
    public void Selector_ShouldReadTheDocumentedWire_AndDefaultEveryMissingField()
    {
        var selector = JsonSerializer.Deserialize<SubscriptionSelector>("""
            {
              "entityTypes": ["event"],
              "domains": [ { "key": "style-fashion", "except": ["style-fashion/beauty"] } ],
              "topics": ["textile"],
              "excludeDomains": [],
              "excludeTopics": [],
              "excludeTimeframes": ["past"],
              "sources": { "include": [ { "groupId": "0f000000-0000-0000-0000-000000000001", "context": { "defaultKind": "trade-fair" } } ], "exclude": [ { "sourceId": "0f000000-0000-0000-0000-000000000002" } ] }
            }
            """, JsonOptionsConstants.Default)!;

        selector.ExcludeTimeframes.Should().Equal("past");
        selector.Domains.Should().ContainSingle().Which.Except.Should().Equal("style-fashion/beauty");
        selector.Sources.Include[0].GroupId.Should().Be(GroupId);
        selector.Sources.Exclude[0].SourceId.Should().Be(SourceId);
        SelectorRules.Validate(selector).Should().BeNull();

        var empty = JsonSerializer.Deserialize<SubscriptionSelector>("{}", JsonOptionsConstants.Default)!;
        empty.ExcludeTimeframes.Should().BeEmpty();
        empty.Sources.Include.Should().BeEmpty();
        SelectorRules.HasTagClause(empty).Should().BeFalse();
    }

    [Fact]
    public void Contract_ShouldRoundTripTheTypesAroundTheSelector()
    {
        var selector = new SubscriptionSelector { EntityTypes = ["event"], ExcludeTimeframes = ["past"] };
        var declaration = new SubscriptionDeclaration
        {
            TenantId = Guid.NewGuid(), ContainerId = Guid.NewGuid(), Name = "ocharo-events", Selector = selector, Schema = "wow2.event.v1",
        };
        var match = new SubscriptionMatch { SelectorVersion = 2, Verdict = SelectorVerdict.NoMatch, Clauses = ["exclude-timeframe:past"] };
        var change = new SelectorChange { Selector = selector, WindowSinceUtc = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero), ChangedBy = "operator" };

        var declarationJson = JsonSerializer.Serialize(declaration, JsonOptionsConstants.Default);
        var matchJson = JsonSerializer.Serialize(match, JsonOptionsConstants.Default);

        JsonNode.Parse(declarationJson)!.AsObject().ContainsKey("pipelineId").Should().BeFalse("a selector declaration carries no pipeline");
        JsonNode.Parse(declarationJson)!["selector"]!["excludeTimeframes"]![0]!.GetValue<string>().Should().Be("past");
        JsonNode.Parse(matchJson)!["verdict"]!.GetValue<string>().Should().Be("noMatch");
        JsonSerializer.Deserialize<SubscriptionDeclaration>(declarationJson, JsonOptionsConstants.Default).Should().BeEquivalentTo(declaration);
        JsonSerializer.Deserialize<SubscriptionMatch>(matchJson, JsonOptionsConstants.Default).Should().BeEquivalentTo(match);
        JsonSerializer.Deserialize<SelectorChange>(JsonSerializer.Serialize(change, JsonOptionsConstants.Default), JsonOptionsConstants.Default).Should().BeEquivalentTo(change);
    }

    private static Dictionary<string, string> Pairs(int count) =>
        Enumerable.Range(0, count).ToDictionary(index => "key" + index, index => "value" + index);

    private static SelectorSourceRef[] Refs(int count) =>
        Enumerable.Range(0, count).Select(_ => new SelectorSourceRef { SourceId = Guid.NewGuid() }).ToArray();
}
