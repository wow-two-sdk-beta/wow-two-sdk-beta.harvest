using System.Text.Json;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace Harvest.Tests.Unit.Extraction;

/// <summary>The machine English title the post-tags pass writes beside a post travels in <c>titles</c> of
/// <c>wow2.event.v1</c>, right after the source <c>title</c>, and is absent when the post has none.</summary>
public sealed class EventSchemaMapperTitlesTests
{
    private static readonly ExtractionStamp Stamp = new() { NormVersion = "test@1", RulesVersion = "test@1" };

    [Fact]
    public void Map_ShouldCarryTheEnglishTitleAsAMachineTitle_BesideTheSourceTitle()
    {
        var post = PostTagsOverlay.Apply(Envelope(), Stored("CAEx Build & Co 2026 Exhibition"));

        var mapped = EventSchemaMapper.Map(post)!.Value;

        mapped.GetProperty("title").GetString().Should().Be("Выставка CAEx Build & Co 2026");
        var titles = mapped.GetProperty("titles").EnumerateArray().ToArray();
        titles.Should().ContainSingle();
        titles[0].GetProperty("text").GetString().Should().Be("CAEx Build & Co 2026 Exhibition");
        titles[0].GetProperty("language").GetString().Should().Be("en");
        titles[0].GetProperty("machine").GetBoolean().Should().BeTrue();
        mapped.EnumerateObject().Select(property => property.Name).Take(3).Should().Equal("schema", "title", "titles");
    }

    [Fact]
    public void Map_ShouldLeaveTitlesOut_WhenThePostHasNoEnglishTitle()
    {
        var untagged = EventSchemaMapper.Map(Envelope())!.Value;
        var withoutTitle = EventSchemaMapper.Map(PostTagsOverlay.Apply(Envelope(), Stored(null)))!.Value;

        untagged.TryGetProperty("titles", out _).Should().BeFalse("a post without tags keeps its earlier wire");
        withoutTitle.TryGetProperty("titles", out _).Should().BeFalse("an answer without an English title adds none");
        withoutTitle.GetRawText().Should().Be(untagged.GetRawText(), "the other fields do not move");
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Map_ShouldLeaveTitlesOut_WhenTheEnglishTitleIsBlank(string blank)
    {
        var post = Envelope() with { Extras = new Dictionary<string, JsonElement>
        {
            [PostTagsConstants.ExtrasNamespace] = JsonSerializer.SerializeToElement(new { titleEn = blank }),
        } };

        EventSchemaMapper.Map(post)!.Value.TryGetProperty("titles", out _).Should().BeFalse();
    }

    [Fact]
    public void Map_ShouldTrimTheEnglishTitle_AndIgnoreAMalformedNamespace()
    {
        var padded = Envelope() with { Extras = new Dictionary<string, JsonElement>
        {
            [PostTagsConstants.ExtrasNamespace] = JsonSerializer.SerializeToElement(new { titleEn = "  Textile Expo \n" }),
        } };
        var malformed = Envelope() with { Extras = new Dictionary<string, JsonElement>
        {
            [PostTagsConstants.ExtrasNamespace] = JsonSerializer.SerializeToElement(new { titleEn = 5 }),
        } };

        EventSchemaMapper.Map(padded)!.Value.GetProperty("titles")[0].GetProperty("text").GetString().Should().Be("Textile Expo");
        EventSchemaMapper.Map(malformed)!.Value.TryGetProperty("titles", out _).Should().BeFalse();
    }

    [Fact]
    public void Map_ShouldReadTheLatestAnswer_WhenTheTagsAreWrittenAgain()
    {
        var first = PostTagsOverlay.Apply(Envelope(), Stored("Textile Expo"));
        var second = PostTagsOverlay.Apply(first, Stored("Textile and Garment Expo"));
        var cleared = PostTagsOverlay.Apply(second, Stored(null));

        EventSchemaMapper.Map(second)!.Value.GetProperty("titles")[0].GetProperty("text").GetString().Should().Be("Textile and Garment Expo");
        EventSchemaMapper.Map(cleared)!.Value.TryGetProperty("titles", out _).Should().BeFalse("a newer answer without a title drops it");
    }

    [Fact]
    public void Map_ShouldReturnNull_ForAPostThatIsNotAnEvent()
    {
        var listing = PostTagsOverlay.Apply(Envelope() with { Kind = PostKind.Listing }, Stored("Flat for rent"));

        EventSchemaMapper.Map(listing).Should().BeNull();
    }

    private static PostEnvelope Envelope() => new()
    {
        Kind = PostKind.Event, Title = "Выставка CAEx Build & Co 2026", Text = "Выставка строительных материалов.", Extraction = Stamp,
    };

    private static StoredPostTags Stored(string? titleEn) => new("event", [new("business-and-finance", true)], ["construction"], titleEn, 0.9m, 3);
}
