using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;

namespace HarvestAdapters.Tests.Unit.Ocharo;

/// <summary><c>ocharo-relevance-v1</c>: what the rules pass, plus a relevant tag verdict at 0.5 or above for Ocharo's pair.</summary>
public sealed class OcharoRelevanceFilterTests
{
    private const string RulesYesTitle = "Fashline 2026";
    private const string RulesNoTitle = "Evening at the gallery";

    [Fact]
    public void Filter_passes_what_the_rules_pass_whatever_the_verdict()
    {
        var filter = new OcharoRelevanceFilter();

        filter.Key.Should().Be("ocharo-relevance-v1");
        filter.Matches(Envelope(RulesYesTitle)).Should().BeTrue();
        filter.Matches(Envelope(RulesYesTitle) with { Classification = Verdict(ClassificationVerdict.Irrelevant, 0.95) }).Should().BeTrue();
    }

    [Theory]
    [InlineData(ClassificationVerdict.Relevant, 0.9, true)]
    [InlineData(ClassificationVerdict.Relevant, 0.5, true)]
    [InlineData(ClassificationVerdict.Relevant, 0.49, false)]
    [InlineData(ClassificationVerdict.Irrelevant, 0.9, false)]
    [InlineData(ClassificationVerdict.Uncertain, 0.9, false)]
    public void Filter_adds_a_rules_rejected_post_only_on_a_relevant_verdict_at_half_or_above(ClassificationVerdict verdict, double confidence,
        bool expected)
    {
        new OcharoFashionFilter().Matches(Tagged(Envelope(RulesNoTitle), "event", "style-fashion")).Should().BeFalse();

        new OcharoRelevanceFilter().Matches(Tagged(Envelope(RulesNoTitle), "event", "style-fashion") with
        {
            Classification = Verdict(verdict, confidence),
        }).Should().Be(expected);
    }

    [Fact]
    public void Filter_keeps_another_products_pair_out()
    {
        var filter = new OcharoRelevanceFilter();
        var relevant = Verdict(ClassificationVerdict.Relevant, 0.9);

        filter.Matches(Tagged(Envelope(RulesNoTitle), "listing", "real-estate") with { Classification = relevant }).Should().BeFalse();
        filter.Matches(Tagged(Envelope(RulesNoTitle), "event", "business-and-finance") with { Classification = relevant }).Should().BeFalse();
        filter.Matches(Envelope(RulesNoTitle) with { Classification = relevant }).Should().BeFalse("a verdict without AI tags is not the tags'");
        filter.Matches(Envelope(RulesNoTitle) with
        {
            Classification = relevant,
            Tags = [new PostTag { Tag = "type:event", Tier = ExtractionTier.Rules }, new PostTag { Tag = "domain:style-fashion", Tier = ExtractionTier.Rules }],
        }).Should().BeFalse("only the model tier's tags count");
    }

    [Theory]
    [InlineData(RulesYesTitle)]
    [InlineData(RulesNoTitle)]
    [InlineData("Craft fair at the bazaar")]
    public void Filter_selects_exactly_the_rules_without_a_classification(string title)
    {
        var post = Envelope(title);

        new OcharoRelevanceFilter().Matches(post).Should().Be(new OcharoFashionFilter().Matches(post));
    }

    private static PostEnvelope Envelope(string title) => new()
    {
        Kind = PostKind.Event, Title = title, Text = title + " in Tashkent.",
        Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
    };

    /// <summary>Adds the model-tier tags the post-tags overlay writes: the type, then the node and its ancestors.</summary>
    private static PostEnvelope Tagged(PostEnvelope envelope, string entityType, string node) => envelope with
    {
        Tags =
        [
            new PostTag { Tag = "type:" + entityType, Tier = ExtractionTier.SmallModel, Version = "post-tags@1" },
            new PostTag { Tag = "domain:" + node, Tier = ExtractionTier.SmallModel, Version = "post-tags@1" },
        ],
    };

    private static PostClassification Verdict(ClassificationVerdict verdict, double confidence) => new()
    {
        Classifier = "model:anthropic/claude-haiku-5-5@post-tags:1", Verdict = verdict, Confidence = confidence, Kind = "event",
    };
}
