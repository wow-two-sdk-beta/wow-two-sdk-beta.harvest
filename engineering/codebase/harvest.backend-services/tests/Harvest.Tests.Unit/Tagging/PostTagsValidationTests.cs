using System.Text.Json;
using System.Text.Json.Nodes;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace Harvest.Tests.Unit.Tagging;

/// <summary>An answer checked against the active taxonomy: unknown nodes become flags, never stored nodes.</summary>
public sealed class PostTagsValidationTests
{
    private static readonly HashSet<string> Active = ["style-fashion", "style-fashion/womens-fashion", "business-and-finance"];

    [Fact]
    public void Validate_ShouldFlagAnUnknownNodeWithItsValue_AndStoreOnlyKnownNodes()
    {
        var tags = PostTagsValidation.Validate(Answer() with
        {
            Domains = [new("fashion-tech", true), new("style-fashion/womens-fashion", false), new("STYLE-FASHION", false)],
        }, Active);

        tags.Domains.Select(domain => domain.Node).Should().Equal("style-fashion/womens-fashion", "style-fashion");
        tags.Domains.Count(domain => domain.Primary).Should().Be(1);
        tags.Domains[0].Primary.Should().BeTrue("the first known node is primary when the model's primary was unknown");
        tags.Flags.Should().ContainSingle().Which.Should().Be(new PostTagsFlag("domains", PostTagsValidation.UnknownNode, "fashion-tech"));
    }

    [Fact]
    public void Validate_ShouldKeepTheModelsPrimary_FirstAndAlone()
    {
        var tags = PostTagsValidation.Validate(Answer() with
        {
            Domains = [new("business-and-finance", false), new("style-fashion", true), new("style-fashion/womens-fashion", true)],
        }, Active);

        tags.Domains.Should().Equal(new PostTagsDomain("style-fashion", true), new PostTagsDomain("business-and-finance", false),
            new PostTagsDomain("style-fashion/womens-fashion", false));
    }

    [Fact]
    public void Validate_ShouldNormalizeTopicsToEnglishSlugs_AndFlagTheRest()
    {
        var tags = PostTagsValidation.Validate(Answer() with { Topics = ["Textile Fair", "textile-fair", "текстиль", "Women's wear", "  "] }, Active);

        tags.Topics.Should().Equal("textile-fair", "womens-wear");
        tags.Flags.Should().ContainSingle().Which.Should().Be(new PostTagsFlag("topics", PostTagsValidation.NotEnglish, "текстиль"));
    }

    [Fact]
    public void Validate_ShouldFlagAnUnsurePick_WhenTheModelGaveNoReason()
    {
        var tags = PostTagsValidation.Validate(Answer() with
        {
            EntityType = PostEntityType.Unsure, Timeframe = PostTimeframe.Unsure,
            Flags = [new("entityType", "a job call", "opportunity")],
        }, Active);

        tags.EntityType.Should().Be("unsure");
        tags.Timeframe.Should().Be("unsure");
        tags.Flags.Should().Equal(new PostTagsFlag("entityType", "a job call", "opportunity"),
            new PostTagsFlag("timeframe", "the model chose unsure", null));
    }

    [Fact]
    public void Validate_ShouldClampNumbersAndDropAnUnreadableLanguage()
    {
        var tags = PostTagsValidation.Validate(Answer() with { Confidence = 1.7, Items = -3, Language = "Russian" }, Active);

        tags.Confidence.Should().Be(1m);
        tags.Items.Should().Be(0);
        tags.Language.Should().BeNull();
    }

    [Fact]
    public void Schema_ShouldRequireEveryFieldAndNameTheClosedValues()
    {
        var schema = JsonNode.Parse(PostTagsDefinition.OutputSchema)!.AsObject();

        schema["required"]!.AsArray().Select(node => node!.GetValue<string>()).Should().BeEquivalentTo(
            "entityType", "domains", "topics", "about", "timeframe", "items", "language", "confidence", "titleEn", "flags");
        schema["additionalProperties"]!.GetValue<bool>().Should().BeFalse();
        schema["properties"]!["entityType"]!["enum"]!.AsArray().Select(node => node!.GetValue<string>())
            .Should().Equal("event", "listing", "none", "unsure");
        schema["properties"]!["timeframe"]!["enum"]!.AsArray().Select(node => node!.GetValue<string>())
            .Should().Equal("upcoming", "ongoing", "past", "evergreen", "unsure");
    }

    [Fact]
    public void Parse_ShouldReadAnAnswer_AndRefuseOneOutsideTheSchema()
    {
        string json = JsonSerializer.Serialize(new
        {
            entityType = "listing", domains = new[] { new { node = "real-estate", primary = true } }, topics = new[] { "rent" },
            about = "a flat", timeframe = "evergreen", items = 1, language = "ru", confidence = 0.8, titleEn = "Flat for rent",
            flags = Array.Empty<object>(),
        });

        PostTagsDefinition.Parse(json)!.EntityType.Should().Be(PostEntityType.Listing);
        PostTagsDefinition.Parse(json.Replace("\"listing\"", "\"job\"")).Should().BeNull();
        PostTagsDefinition.Parse(json.Replace("\"flags\"", "\"extra\":1,\"flags\"")).Should().BeNull();
        PostTagsDefinition.Parse("not json").Should().BeNull();
    }

    internal static PostTagsAnswer Answer() => new(PostEntityType.Event, [new("style-fashion", true)], ["fashion-week"], "a fashion week",
        PostTimeframe.Upcoming, 1, "uz", 0.9, "Tashkent Fashion Week", []);
}
