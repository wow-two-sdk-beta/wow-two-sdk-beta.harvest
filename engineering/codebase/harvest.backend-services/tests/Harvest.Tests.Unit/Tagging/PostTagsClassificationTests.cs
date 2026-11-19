using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace Harvest.Tests.Unit.Tagging;

/// <summary>The relevance verdict a post's tags give for the wanted pairs, and the classification it becomes.</summary>
public sealed class PostTagsClassificationTests
{
    private static readonly PostExtractionWant Ocharo = new() { EntityType = "event", Domain = "style-fashion" };
    private static readonly PostExtractionWant Haven = new() { EntityType = "listing", Domain = "real-estate" };

    [Theory]
    [InlineData("event", "style-fashion/womens-fashion", ClassificationVerdict.Relevant, "event under style-fashion")]
    [InlineData("event", "style-fashion", ClassificationVerdict.Relevant, "event under style-fashion")]
    [InlineData("event", "business-and-finance", ClassificationVerdict.Irrelevant, PostTagsClassification.OffEveryPair)]
    [InlineData("listing", "style-fashion", ClassificationVerdict.Irrelevant, PostTagsClassification.OffEveryPair)]
    [InlineData("none", "style-fashion", ClassificationVerdict.Irrelevant, PostTagsClassification.NoEntity)]
    [InlineData("unsure", "style-fashion", ClassificationVerdict.Uncertain, PostTagsClassification.EntityTypeUnsure)]
    [InlineData("event", null, ClassificationVerdict.Uncertain, PostTagsClassification.DomainUnsure)]
    [InlineData("none", null, ClassificationVerdict.Irrelevant, PostTagsClassification.NoEntity)]
    public void Verdict_ShouldFollowTheEntityTypeAndDomain(string entityType, string? node, ClassificationVerdict expected, string reason)
    {
        PostTagsDomain[] domains = node is null ? [] : [new(node, true)];

        var verdict = PostTagsClassification.Verdict(entityType, domains, [Ocharo]);

        verdict.Should().Be((expected, reason));
    }

    [Fact]
    public void Verdict_ShouldMatchAnyWantedPair_AndASecondaryNode()
    {
        PostTagsClassification.Verdict("listing", [new("real-estate/apartments", true)], [Ocharo, Haven])
            .Should().Be((ClassificationVerdict.Relevant, "listing under real-estate"));
        PostTagsClassification.Verdict("event", [new("business-and-finance", true), new("style-fashion", false)], [Ocharo])!
            .Value.Verdict.Should().Be(ClassificationVerdict.Relevant);
        PostTagsClassification.Verdict("event", [new("style-fashion", true)], []).Should().BeNull("nothing is wanted");
    }

    [Fact]
    public void Classify_ShouldNameTheModelAndPrompt_AndTakeTheExtractionFormatAsKind()
    {
        var stored = Stored("event", "style-fashion/womens-fashion");

        var tagged = PostTagsClassification.Classify(stored, [Ocharo], format: null)!;
        tagged.Classifier.Should().Be("model:anthropic/claude-haiku-5-5@post-tags:3");
        tagged.Verdict.Should().Be(ClassificationVerdict.Relevant);
        tagged.Confidence.Should().Be(0.9);
        tagged.Reason.Should().Be("event under style-fashion");
        tagged.Kind.Should().Be("event", "without an extraction the entity type is the kind");

        PostTagsClassification.Classify(stored, [Ocharo], "runway")!.Kind.Should().Be("runway");
        PostTagsClassification.Classify(stored, [Ocharo], "trade-fair")!.Kind.Should().Be("trade-fair");
        PostTagsClassification.Classify(stored, [Ocharo], "unsure")!.Kind.Should().Be("event", "an unsure format names no kind");
        PostTagsClassification.Classify(Stored("unsure", "style-fashion"), [Ocharo], null)!.Kind.Should().BeNull();
        PostTagsClassification.Classify(stored with { Model = null, PromptVersion = null }, [Ocharo], null)!.Classifier
            .Should().Be("model:unknown@post-tags:unknown");
    }

    [Fact]
    public void Apply_ShouldLeaveTheEnvelopeAlone_WithoutTagsOrPairs()
    {
        var post = new PostEnvelope { Kind = PostKind.Event, Title = "A fair", Extraction = new() { NormVersion = "n", RulesVersion = "r" } };

        PostTagsClassification.Apply(post, null, [Ocharo], "runway").Should().BeSameAs(post);
        PostTagsClassification.Apply(post, Stored("event", "style-fashion"), [], "runway").Should().BeSameAs(post);
        PostTagsClassification.Apply(post, Stored("none", "style-fashion"), [Ocharo], null).Classification!.Verdict
            .Should().Be(ClassificationVerdict.Irrelevant);
    }

    private static StoredPostTags Stored(string entityType, string node) =>
        new(entityType, [new(node, true)], ["fashion-week"], "Tashkent Fashion Week", 0.9m, 3, "anthropic/claude-haiku-5-5");
}
