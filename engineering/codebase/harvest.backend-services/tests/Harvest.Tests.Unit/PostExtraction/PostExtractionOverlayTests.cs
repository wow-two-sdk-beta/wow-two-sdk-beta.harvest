using System.Text.Json;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace Harvest.Tests.Unit.PostExtraction;

/// <summary>The estimated dates an <c>extract-event</c> answer writes onto an undated post, and how the wire and the
/// prompts read them.</summary>
public sealed class PostExtractionOverlayTests
{
    private const string Occurrence = """{"starts":"2026-12","ends":"2027-01","precision":"month","certainty":"estimated","evidence":"in December"}""";

    [Fact]
    public void Apply_ShouldEstimateTheStartAndEnd_OfAnUndatedPost()
    {
        var estimated = PostExtractionOverlay.Apply(Envelope(), Stored());

        estimated.Dates.Should().HaveCount(2);
        estimated.Dates[0].Should().Be(new PostDate
        {
            Role = DateRole.Start, Value = "2026-12", Precision = DatePrecision.Month, Raw = "in December", Certainty = DateCertainty.Estimated,
        });
        estimated.Dates[1].Role.Should().Be(DateRole.End);
        estimated.Dates[1].Value.Should().Be("2027-01");
        estimated.Dates[1].Certainty.Should().Be(DateCertainty.Estimated);

        var wire = EventSchemaMapper.Map(estimated)!.Value;
        wire.GetProperty("starts").GetProperty("certainty").GetString().Should().Be("estimated");
        wire.GetProperty("starts").GetProperty("precision").GetString().Should().Be("month");
        wire.GetProperty("ends").GetProperty("certainty").GetString().Should().Be("estimated");
    }

    [Fact]
    public void Apply_ShouldKeepAStatedStart_AndAStatedEnd()
    {
        var stated = new PostDate { Role = DateRole.Start, Value = "2026-12-12", Precision = DatePrecision.Day };
        var post = Envelope() with { Dates = [stated] };

        PostExtractionOverlay.Apply(post, Stored()).Should().BeSameAs(post, "a stated start always wins");
        EventSchemaMapper.Map(post)!.Value.GetProperty("starts").GetProperty("certainty").GetString().Should().Be("stated");

        var end = new PostDate { Role = DateRole.End, Value = "2027-01-05", Precision = DatePrecision.Day };
        var mixed = PostExtractionOverlay.Apply(Envelope() with { Dates = [end] }, Stored());
        mixed.Dates.Should().HaveCount(2);
        mixed.Dates.Single(date => date.Role == DateRole.End).Should().BeSameAs(end);
        mixed.Dates.Single(date => date.Role == DateRole.Start).Certainty.Should().Be(DateCertainty.Estimated);
    }

    [Fact]
    public void Apply_ShouldDoNothing_WithoutAnOccurrence()
    {
        var post = Envelope();

        PostExtractionOverlay.Apply(post, null).Should().BeSameAs(post);
        PostExtractionOverlay.Apply(post, StoredEventExtraction.Read(true, "runway", null)).Should().BeSameAs(post);
        StoredEventExtraction.Read(false, "runway", Occurrence).Should().BeNull();
        StoredEventExtraction.Read(true, "runway", "{\"broken\":1}")!.First.Should().BeNull();
    }

    [Fact]
    public void StatedDate_ShouldWriteNoCertainty_SoStoredPostsKeepTheirBytes()
    {
        var stated = new PostDate { Role = DateRole.Start, Value = "2026-12-12", Precision = DatePrecision.Day };

        JsonSerializer.Serialize(stated, WoW.Two.Sdk.Beta.Be.Foundation.Serialization.JsonOptionsConstants.Default)
            .Should().NotContain("certainty");
    }

    [Fact]
    public void PromptDates_ShouldLeaveOutEstimates()
    {
        var estimated = PostExtractionOverlay.Apply(Envelope(), Stored());

        PostTagsContent.Dates(estimated.Dates).Should().Be("none", "no prompt reads a model's own estimate as the post's");
        var stated = new PostDate { Role = DateRole.Deadline, Value = "2026-11-30", Precision = DatePrecision.Day };
        PostTagsContent.Dates([.. estimated.Dates, stated]).Should().Be("deadline: 2026-11-30");
    }

    private static PostEnvelope Envelope() => new()
    {
        Kind = PostKind.Event, Title = "Tashkent Fashion Week", Text = "The show returns in December.",
        Extraction = new ExtractionStamp { NormVersion = "test@1", RulesVersion = "test@1" },
    };

    private static StoredEventExtraction Stored() => StoredEventExtraction.Read(true, "runway", Occurrence)!;
}
