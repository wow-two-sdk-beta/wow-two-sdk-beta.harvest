using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace Harvest.Tests.Unit.PostExtraction;

/// <summary>The demand gate's matching: a post qualifies when its tags have a wanted entity type and a node at or under
/// the wanted key.</summary>
public sealed class PostExtractionDemandTests
{
    private static readonly IReadOnlyList<PostExtractionWant> Wanted = PostExtractionDemand.Normalize(
    [
        new() { EntityType = "Event", Domain = " Style-Fashion " },
        new() { EntityType = "listing", Domain = "real-estate" },
        new() { EntityType = "event", Domain = "style-fashion" },
        new() { EntityType = "opportunity", Domain = "careers" },
        new() { EntityType = "event", Domain = "Style & Fashion" },
    ]);

    [Fact]
    public void Normalize_ShouldLowercaseTrimDeduplicate_AndDropMalformedPairs()
    {
        Wanted.Should().Equal(new PostExtractionWant { EntityType = "event", Domain = "style-fashion" },
            new PostExtractionWant { EntityType = "listing", Domain = "real-estate" });
    }

    [Theory]
    [InlineData("event", "style-fashion", "extract-event")]
    [InlineData("event", "style-fashion/womens-fashion", "extract-event")]
    [InlineData("listing", "real-estate/apartments/rent", "extract-listing")]
    [InlineData("event", "style-fashion-tech", null)]
    [InlineData("event", "style", null)]
    [InlineData("event", "real-estate", null)]
    [InlineData("listing", "style-fashion", null)]
    [InlineData("none", "style-fashion", null)]
    [InlineData("unsure", "style-fashion", null)]
    public void Match_ShouldWantTheTypeWithANodeAtOrUnderTheKey(string entityType, string node, string? expected)
    {
        PostExtractionDemand.Match(Wanted, entityType, [new PostTagsDomain(node, true)]).Should().Be(expected);
    }

    [Fact]
    public void Match_ShouldAcceptASecondaryNode_AndNothingWhenNothingIsWanted()
    {
        PostTagsDomain[] domains = [new("business-and-finance", true), new("style-fashion/mens-fashion", false)];

        PostExtractionDemand.Match(Wanted, "event", domains).Should().Be("extract-event");
        PostExtractionDemand.Match([], "event", domains).Should().BeNull();
        PostExtractionDemand.Match(Wanted, "event", []).Should().BeNull();
    }

    [Fact]
    public void IsValid_ShouldTakeOnlyEventOrListingWithATaxonomyKey()
    {
        PostExtractionDemand.IsValid(new() { EntityType = "listing", Domain = "real-estate/apartments" }).Should().BeTrue();
        PostExtractionDemand.IsValid(new() { EntityType = "listing", Domain = "" }).Should().BeFalse();
        PostExtractionDemand.IsValid(new() { EntityType = "none", Domain = "real-estate" }).Should().BeFalse();
        PostExtractionDemand.IsValid(new() { EntityType = "event", Domain = "style-fashion/" }).Should().BeFalse();
    }
}
