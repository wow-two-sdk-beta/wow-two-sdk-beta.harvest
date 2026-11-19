using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace Harvest.Tests.Unit.Tagging;

/// <summary>The content hash that guards the write-back loop, the tags written back, and the prompt variables.</summary>
public sealed class PostTagsContentTests
{
    [Fact]
    public void Hash_ShouldStayTheSame_WhenAWriteBackOnlyAddsTags()
    {
        var post = Envelope();
        var tagged = PostTagsOverlay.Apply(post, Stored());

        tagged.Tags.Should().NotBeEmpty();
        PostTagsContent.Hash(tagged).Should().Equal(PostTagsContent.Hash(post));
        PostTagsContent.Hash(post with { Prices = [new PostPrice { Amount = 50_000, Currency = "UZS" }], Flags = ["x"] })
            .Should().Equal(PostTagsContent.Hash(post), "only the title and text queue tagging");
    }

    [Fact]
    public void Hash_ShouldChange_WhenTheTitleOrTextChanges()
    {
        var post = Envelope();

        PostTagsContent.Hash(post with { Title = "Other" }).Should().NotEqual(PostTagsContent.Hash(post));
        PostTagsContent.Hash(post with { Text = post.Text + "!" }).Should().NotEqual(PostTagsContent.Hash(post));
        PostTagsContent.Hash("ab", "c").Should().NotEqual(PostTagsContent.Hash("a", "bc"));
        PostTagsContent.Hash(post).Should().HaveCount(32);
    }

    [Fact]
    public void Overlay_ShouldWriteTypeDomainLineageAndTopicTags_BesideMappedTags()
    {
        var mapped = new PostTag { Tag = "fashion", Tier = ExtractionTier.Rules };
        var tagged = PostTagsOverlay.Apply(Envelope() with { Tags = [mapped] }, Stored());

        tagged.Tags.Select(tag => tag.Tag).Should().Equal("fashion", "type:event", "domain:style-fashion/womens-fashion",
            "domain:style-fashion", "domain:business-and-finance", "topic:fashion-week");
        tagged.Tags.Skip(1).Should().OnlyContain(tag => tag.Tier == ExtractionTier.SmallModel && tag.Version == "post-tags@3"
            && tag.Score == 0.9);
        tagged.Tags[0].Should().Be(mapped);
        tagged.Extras[PostTagsConstants.ExtrasNamespace].GetProperty("titleEn").GetString().Should().Be("Tashkent Fashion Week");
        PostTagsOverlay.Apply(tagged, Stored()).Tags.Should().Equal(tagged.Tags, "a second resolve writes the same tags");
    }

    [Fact]
    public void Overlay_ShouldWriteNoTypeTag_WhenTheEntityTypeIsUnsure()
    {
        var tagged = PostTagsOverlay.Apply(Envelope(), Stored() with { EntityType = "unsure", TitleEn = null });

        tagged.Tags.Should().NotContain(tag => tag.Tag.StartsWith(PostTagsConstants.TypeTagPrefix, StringComparison.Ordinal));
        tagged.Extras.Should().NotContainKey(PostTagsConstants.ExtrasNamespace);
        var untagged = Envelope();
        PostTagsOverlay.Apply(untagged, null).Should().BeSameAs(untagged);
    }

    [Fact]
    public void Variables_ShouldNameEveryPlaceholder_AndReadUnknownForMissingValues()
    {
        var empty = PostTagsContent.Variables(new PostEnvelope { Extraction = Stamp }, null, "taxonomy", 4000);

        empty.Keys.Should().BeEquivalentTo(PostTagsDefinition.Variables);
        empty["sourceCategory"].Should().Be("unknown");
        empty["dates"].Should().Be("none");
        empty["place"].Should().Be("unknown");
        empty["postedAt"].Should().Be("unknown");
        empty["language"].Should().Be("und");
        empty["title"].Should().BeEmpty();

        var post = Envelope() with
        {
            Text = new string('x', 5000),
            PostedAt = new PostTimestamp { Value = "2026-11-03T23:30:00+05:00", Precision = DatePrecision.Minute },
            Dates = [new PostDate { Role = DateRole.Start, Value = "2026-12-01", End = "2026-12-03", Precision = DatePrecision.Day }],
            Location = new PostLocation { Mentions = [new LocationMention { Text = "Tashkent", Kind = LocationKind.City }] },
        };
        var full = PostTagsContent.Variables(post, "Fashion", "a — A", 4000);

        full["text"].Should().HaveLength(4000);
        full["postedAt"].Should().Be("2026-11-03", "the publisher's own calendar date");
        full["dates"].Should().Be("start: 2026-12-01 – 2026-12-03");
        full["place"].Should().Be("Tashkent");
        full["sourceCategory"].Should().Be("Fashion");
        full["taxonomy"].Should().Be("a — A");
    }

    [Fact]
    public void PostedAt_ShouldReadTheFirstCapture_WhenThePublisherGaveNoDate()
    {
        var seen = new DateTimeOffset(2026, 11, 16, 19, 15, 0, TimeSpan.Zero);

        PostTagsContent.Variables(new PostEnvelope { Extraction = Stamp }, null, "taxonomy", 4000, seen)["postedAt"]
            .Should().Be("unknown; first seen 2026-11-16");
        PostTagsContent.PostedAt(new PostTimestamp { Value = "2026-11-03", Precision = DatePrecision.Day }, seen)
            .Should().Be("2026-11-03", "a stated publication date wins over the first capture");
        PostTagsContent.PostedAt(null).Should().Be("unknown");
    }

    [Theory]
    [InlineData("harvest:post:0193a0b1-0000-7000-8000-000000000001@4", true, 4)]
    [InlineData("harvest:post:0193a0b1-0000-7000-8000-000000000001", true, null)]
    [InlineData("harvest:post:0193a0b1-0000-7000-8000-000000000001@0", false, null)]
    [InlineData("harvest:capture:0193a0b1-0000-7000-8000-000000000001", false, null)]
    [InlineData("harvest:post:not-a-guid@2", false, null)]
    public void Subject_ShouldParseThePostAndItsRevision(string subject, bool parsed, int? revision)
    {
        PostTagsSubjectSource.TryParse(subject, out _, out var actual).Should().Be(parsed);
        if (parsed)
            actual.Should().Be(revision);
    }

    private static readonly ExtractionStamp Stamp = new() { NormVersion = "test@1", RulesVersion = "test@1" };

    private static PostEnvelope Envelope() => new()
    {
        Kind = PostKind.Event, Title = "Tashkent Fashion Week", Text = "Moda haftaligi dekabrda.", Extraction = Stamp,
    };

    private static StoredPostTags Stored() => new("event",
        [new("business-and-finance", false), new("style-fashion/womens-fashion", true)], ["fashion-week"], "Tashkent Fashion Week", 0.9m, 3);
}
