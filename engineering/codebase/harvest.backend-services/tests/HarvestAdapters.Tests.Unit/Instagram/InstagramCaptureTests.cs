using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;

namespace HarvestAdapters.Tests.Unit.Instagram;

public sealed class InstagramCaptureTests
{
    [Fact]
    public async Task Captions_preserve_post_time_without_inventing_event_dates_and_need_Ocharo_review()
    {
        var capture = InstagramCaptureReader.Read(Media("Open call for designers: Fashion Week Tashkent. Applications open.", 5),
            "teplofest", "https://www.instagram.com/teplofest/", DateTimeOffset.UtcNow)!;
        capture.ExternalKey.Should().Be("instagram:123");
        capture.PublishedAtUtc.Should().NotBeNull();
        capture.Warnings.Should().Contain(warning => warning.Contains("publication time"));
        var result = await new RulesPostExtractor().ExtractAsync(capture, new() { Family = SourceFamily.Instagram });
        var envelope = ((WoW.Two.Sdk.Beta.Be.Foundation.Results.Result<PostEnvelope>.Success)result).Value;
        envelope.Kind.Should().Be(PostKind.Event);
        envelope.Dates.Should().BeEmpty();
        new OcharoFashionFilter().Matches(envelope).Should().BeTrue();
        envelope.Extras["schemaOrg"].GetProperty("instagram").GetProperty("requiresReview").GetBoolean().Should().BeTrue();
        Encoding.UTF8.GetString(capture.Body.Span).Should().NotContain("access_token").And.NotContain("like_count").And.NotContain("paging");
    }

    [Fact]
    public void Stable_identity_ignores_engagement_and_signed_media_but_detects_caption_edits()
    {
        var first = InstagramCaptureReader.Read(Media("Join us at the handmade market", 1), "teplofest", "https://www.instagram.com/teplofest/", DateTimeOffset.UtcNow)!;
        var changedMetrics = InstagramCaptureReader.Read(Media("Join us at the handmade market", 999), "teplofest", first.RequestUrl, DateTimeOffset.UtcNow)!;
        var edited = InstagramCaptureReader.Read(Media("Join us at the handmade market — new venue", 999), "teplofest", first.RequestUrl, DateTimeOffset.UtcNow)!;
        first.ContentProjection.Should().Be(changedMetrics.ContentProjection);
        first.ContentProjection.Should().NotBe(edited.ContentProjection);
        first.ContentProjection.Should().NotContain("cdninstagram").And.NotContain("signature");
    }

    [Theory]
    [InlineData("https://attacker.example/p/abc/")]
    [InlineData("https://www.instagram.com/accounts/login/")]
    [InlineData("https://user:password@www.instagram.com/p/abc/")]
    public void Off_platform_or_non_post_urls_are_not_captures(string url)
    {
        var record = JsonSerializer.SerializeToElement(new { id = "123", permalink = url });
        InstagramCaptureReader.Read(record, "teplofest", "https://www.instagram.com/teplofest/", DateTimeOffset.UtcNow).Should().BeNull();
    }

    [Theory]
    [InlineData("New designer collection now in store")]
    [InlineData("Fashion week highlights: tickets were sold out")]
    public async Task Editorial_and_recap_captions_do_not_enter_the_event_stream(string caption)
    {
        var capture = InstagramCaptureReader.Read(Media(caption, 1), "teplofest", "https://www.instagram.com/teplofest/", DateTimeOffset.UtcNow)!;
        var result = await new RulesPostExtractor().ExtractAsync(capture, new() { Family = SourceFamily.Instagram });
        new OcharoFashionFilter().Matches(((WoW.Two.Sdk.Beta.Be.Foundation.Results.Result<PostEnvelope>.Success)result).Value).Should().BeFalse();
    }

    internal static JsonElement Media(string caption, int likes) => JsonSerializer.SerializeToElement(new
    {
        id = "123", caption, timestamp = "2026-10-27T10:00:00+0000", permalink = "https://www.instagram.com/p/Abc123/",
        media_type = "IMAGE", media_url = $"https://scontent.cdninstagram.com/post.jpg?signature={likes}", like_count = likes,
        paging = new { next = "https://graph.facebook.com?access_token=must-not-persist" },
    });
}
