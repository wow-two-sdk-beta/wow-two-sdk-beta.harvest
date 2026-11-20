using System.Text;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Telegram;

namespace HarvestAdapters.Tests.Unit.Sources.Telegram;

/// <summary>Reads synthetic preview pages shaped like the markup of <c>t.me/s/{username}</c>, with an invented channel and
/// invented text. A fixture parse establishes the markup shape, not live access.</summary>
public sealed class TelegramPreviewCaptureReaderTests
{
    private const string Channel = "demo_channel";
    private static readonly TelegramPreviewCaptureReader Reader = new();

    [Fact]
    public async Task Every_message_block_reads_to_its_id_time_permalink_and_text()
    {
        var read = await ReadAsync(Page(
            Message(101, "Demo fair opens its doors<br/>Tickets at the door", time: "2026-11-18T09:30:00+00:00"),
            Message(102, "Second notice", time: "2026-11-19T14:05:11+05:00")));

        read.Complete.Should().BeFalse("a preview is a moving window");
        read.Captures.Should().HaveCount(2);
        var first = read.Captures[0];
        first.ExternalKey.Should().Be("demo_channel/101");
        first.Url.Should().Be("https://t.me/demo_channel/101", "the permalink is the credit link of the item");
        first.OriginKey.Should().Be("https://t.me/demo_channel/101");
        first.PublishedAtUtc.Should().Be(new DateTimeOffset(2026, 11, 18, 9, 30, 0, TimeSpan.Zero));
        first.Structured[0].GetProperty("title").GetString().Should().Be("Demo fair opens its doors");
        first.Structured[0].GetProperty("text").GetString().Should().Be("Demo fair opens its doors\nTickets at the door");
        first.Structured[0].GetProperty("author").GetString().Should().Be("Demo Channel");
        first.Structured[0].GetProperty("reader").GetString().Should().Be("page");
        first.Structured[0].GetProperty("telegram").GetProperty("eventDateStatus").GetString().Should().Be("not-extracted-from-text");
        first.RequestUrl.Should().Be("https://t.me/s/demo_channel");
        first.HttpStatus.Should().Be(200);
        first.Warnings.Should().BeEmpty();
        read.Captures[1].PublishedAtUtc.Should().Be(new DateTimeOffset(2026, 11, 19, 9, 5, 11, TimeSpan.Zero), "the time is read as an instant");
        read.Captures[1].ExternalKey.Should().Be("demo_channel/102");
    }

    [Fact]
    public async Task The_publication_time_is_never_taken_for_an_event_date()
    {
        var read = await ReadAsync(Page(Message(101, "Fair on 25 November", time: "2026-11-18T09:30:00+00:00")));

        var capture = read.Captures.Should().ContainSingle().Subject;
        capture.Structured[0].TryGetProperty("startDate", out _).Should().BeFalse();
        capture.Structured[0].TryGetProperty("@type", out _).Should().BeFalse("a post is a page-style record, never an Event node");
        capture.PublishedAtUtc.Should().Be(new DateTimeOffset(2026, 11, 18, 9, 30, 0, TimeSpan.Zero));
    }

    [Fact]
    public async Task Text_keeps_line_breaks_and_links_and_drops_hashtag_searches()
    {
        string body = "Open call<br/><br/>Apply at <a href=\"https://apply.example.test/form?round=2\" target=\"_blank\" rel=\"noopener\" "
            + "onclick=\"return confirm('Open this link?')\">the form</a> by 20 December "
            + "<tg-emoji emoji-id=\"1\"><i class=\"emoji\" style=\"background-image:url('//telegram.org/img/emoji/40/E29C88.png')\"><b>✈️</b></i></tg-emoji> "
            + "<a href=\"?q=%23demo\">#demo</a> <a href=\"https://t.me/demo_user\">@demo_user</a> "
            + "<a href=\"https://docs.example.test/page\">docs.example.test/page</a> <a href=\"https://docs.example.test/long/path\">https://docs.example.test/lon…</a>"
            + "<blockquote>Quoted line</blockquote><a href=\"tel:+000\">call</a>";

        var capture = (await ReadAsync(Page(Message(101, body)))).Captures.Should().ContainSingle().Subject;

        capture.Structured[0].GetProperty("text").GetString().Should().Be(
            "Open call\n\nApply at the form (https://apply.example.test/form?round=2) by 20 December ✈️ #demo @demo_user "
            + "docs.example.test/page https://docs.example.test/lon…\nQuoted line\ncall");
        capture.Links.Select(link => link.Url).Should().Equal(
            "https://apply.example.test/form?round=2", "https://t.me/demo_user", "https://docs.example.test/page", "https://docs.example.test/long/path");
        capture.Links[0].Text.Should().Be("the form");
    }

    [Fact]
    public async Task A_replys_embedded_text_is_not_the_messages_text()
    {
        string reply = "<a class=\"tgme_widget_message_reply\" href=\"https://t.me/demo_channel/90\"><div class=\"tgme_widget_message_author\"><span>Earlier</span></div>"
            + "<div class=\"tgme_widget_message_text js-message_reply_text\" dir=\"auto\">The quoted older notice</div></a>";

        var capture = (await ReadAsync(Page(Message(101, "The actual answer", extra: reply)))).Captures.Should().ContainSingle().Subject;

        capture.Structured[0].GetProperty("text").GetString().Should().Be("The actual answer");
    }

    [Fact]
    public async Task Photos_and_videos_are_kept_by_reference_and_a_link_previews_image_is_not_the_messages_media()
    {
        string media = """
            <a class="tgme_widget_message_photo_wrap 1 2" href="https://t.me/demo_channel/101?single" style="width:800px;background-image:url('https://cdn4.telesco.pe/file/photo-one.jpg')"><div class="tgme_widget_message_photo"></div></a>
            <a class="tgme_widget_message_photo_wrap" href="https://t.me/demo_channel/102?single" style="background-image:url('https://other.example.test/not-telegram.jpg')"></a>
            <a class="tgme_widget_message_video_player not_supported" href="https://t.me/demo_channel/103"><i class="tgme_widget_message_video_thumb" style="background-image:url('https://cdn4.telesco.pe/file/video-thumb.jpg')"></i>
              <video src="https://cdn4.telesco.pe/file/clip.mp4?token=abc" class="tgme_widget_message_video"></video><video src="https://cdn4.telesco.pe/file/clip.mp4?token=abc" class="tgme_widget_message_video blured"></video></a>
            """;
        string preview = """
            <a class="tgme_widget_message_link_preview" href="https://news.example.test/a"><i class="link_preview_image" style="background-image:url('https://cdn4.telesco.pe/file/preview.jpg')"></i></a>
            <a class="tgme_widget_message_reply" href="https://t.me/demo_channel/90"><i class="tgme_widget_message_reply_thumb" style="background-image:url('https://cdn4.telesco.pe/file/reply.jpg')"></i></a>
            """;

        var capture = (await ReadAsync(Page(Message(101, "Photo and clip", media: media, extra: preview)))).Captures.Should().ContainSingle().Subject;

        capture.Media.Select(item => (item.Kind, item.Url, item.Position)).Should().Equal(
            (MediaKind.Image, "https://cdn4.telesco.pe/file/photo-one.jpg", 0),
            (MediaKind.Video, "https://cdn4.telesco.pe/file/clip.mp4?token=abc", 1),
            (MediaKind.Thumbnail, "https://cdn4.telesco.pe/file/video-thumb.jpg", 2));
    }

    [Fact]
    public async Task A_post_without_text_keeps_a_title_and_forwarding_travels_as_evidence()
    {
        string forwarded = "<div class=\"tgme_widget_message_forwarded_from accent_color\">Forwarded from&nbsp;<a class=\"tgme_widget_message_forwarded_from_name\" href=\"https://t.me/other_channel/5\"><span dir=\"auto\">Other Channel</span></a></div>";

        var capture = (await ReadAsync(Page(Message(105, null, extra: forwarded)))).Captures.Should().ContainSingle().Subject;

        capture.Structured[0].GetProperty("text").GetString().Should().BeEmpty();
        capture.Structured[0].GetProperty("title").GetString().Should().Be("@demo_channel · 105");
        capture.Structured[0].GetProperty("telegram").GetProperty("forwardedFrom").GetString().Should().Be("Other Channel");
        capture.Url.Should().Be("https://t.me/demo_channel/105", "a forwarded post is the channel's own message");
    }

    [Fact]
    public async Task A_message_without_a_time_still_reads_and_says_so()
    {
        var capture = (await ReadAsync(Page(Message(101, "Undated", time: null)))).Captures.Should().ContainSingle().Subject;

        capture.PublishedAtUtc.Should().BeNull();
        capture.Warnings.Should().Equal("message_time_unknown");
    }

    [Fact]
    public async Task Foreign_and_malformed_blocks_are_skipped_and_a_repeated_id_reads_once()
    {
        var read = await ReadAsync(Page(
            Message(101, "Kept"),
            Message(101, "Repeated id"),
            Message(102, "Other channel's block", user: "other_channel"),
            MessageBlock("demo_channel/abc", "Not a number"),
            MessageBlock("demo_channel/0", "Zero"),
            "<div class=\"tgme_widget_message\">No post attribute</div>"));

        read.Captures.Should().ContainSingle().Which.ExternalKey.Should().Be("demo_channel/101");
    }

    [Fact]
    public async Task The_newest_page_points_to_the_page_before_its_smallest_id()
    {
        var read = await ReadAsync(Page(Message(101, "a"), Message(103, "c"), Message(102, "b")));

        var next = read.Discovered.Should().ContainSingle().Subject;
        next.Url.Should().Be("https://t.me/s/demo_channel?before=101");
        next.IsListing.Should().BeTrue();
    }

    [Fact]
    public async Task An_older_page_keeps_paging_only_while_its_ids_move_back()
    {
        var older = await ReadAsync(Page(Message(81, "a"), Message(100, "b")), "https://t.me/s/demo_channel?before=101");
        var repeated = await ReadAsync(Page(Message(101, "a"), Message(110, "b")), "https://t.me/s/demo_channel?before=101");
        var first = await ReadAsync(Page(Message(1, "the first message"), Message(2, "the second")), "https://t.me/s/demo_channel?before=3");

        older.Discovered.Should().ContainSingle().Which.Url.Should().Be("https://t.me/s/demo_channel?before=81");
        repeated.Captures.Should().HaveCount(2);
        repeated.Discovered.Should().BeEmpty("a page that does not lie before the one asked for would loop");
        first.Captures.Should().HaveCount(2);
        first.Discovered.Should().BeEmpty("nothing lies before message 1");
    }

    [Fact]
    public async Task An_empty_page_ends_the_run()
    {
        var read = await ReadAsync(Page(), "https://t.me/s/demo_channel?before=5");

        read.Captures.Should().BeEmpty();
        read.Discovered.Should().BeEmpty();
        read.Complete.Should().BeFalse();
    }

    [Fact]
    public async Task Counters_and_the_clock_label_never_make_a_revision()
    {
        var quiet = (await ReadAsync(Page(Message(101, "Fair notice", views: 12, clock: "09:30")))).Captures.Single();
        var busy = (await ReadAsync(Page(Message(101, "Fair notice", views: 98_765, clock: "10:02",
            extra: "<div class=\"tgme_widget_message_reactions js-message_reactions\"><span class=\"tgme_reaction\"><b>👍</b>41</span></div>"))))
            .Captures.Single();

        busy.ContentProjection.Should().Be(quiet.ContentProjection);
        busy.ExternalKey.Should().Be(quiet.ExternalKey);
        busy.ContentProjection.Should().NotContain("98765").And.NotContain("98 765").And.NotContain("views");
    }

    [Fact]
    public async Task Changing_a_media_address_alone_makes_no_revision_but_a_different_media_count_does()
    {
        const string photo = "<a class=\"tgme_widget_message_photo_wrap\" style=\"background-image:url('https://cdn{0}.telesco.pe/file/p{1}.jpg')\"></a>";
        var before = (await ReadAsync(Page(Message(101, "Poster", media: string.Format(photo, 4, 1))))).Captures.Single();
        var rotated = (await ReadAsync(Page(Message(101, "Poster", media: string.Format(photo, 5, 2))))).Captures.Single();
        var added = (await ReadAsync(Page(Message(101, "Poster", media: string.Format(photo, 4, 1) + string.Format(photo, 4, 3))))).Captures.Single();

        rotated.ContentProjection.Should().Be(before.ContentProjection, "the preview may rotate its CDN addresses");
        added.ContentProjection.Should().NotBe(before.ContentProjection);
    }

    [Fact]
    public async Task An_edited_text_makes_a_revision_under_the_same_identity()
    {
        var original = (await ReadAsync(Page(Message(101, "Fair on 25 November")))).Captures.Single();
        var edited = (await ReadAsync(Page(Message(101, "Fair moved to 2 December", edited: true)))).Captures.Single();
        var markerOnly = (await ReadAsync(Page(Message(101, "Fair on 25 November", edited: true)))).Captures.Single();

        edited.ExternalKey.Should().Be(original.ExternalKey);
        edited.Url.Should().Be(original.Url);
        edited.ContentProjection.Should().NotBe(original.ContentProjection);
        markerOnly.ContentProjection.Should().NotBe(original.ContentProjection, "an edit with the same text is still a change of the post");
        markerOnly.Structured[0].GetProperty("telegram").GetProperty("edited").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task The_body_is_the_messages_own_slice_not_the_whole_page()
    {
        var read = await ReadAsync(Page(Message(101, "One"), Message(102, "Two")));

        string[] bodies = [.. read.Captures.Select(capture => Encoding.UTF8.GetString(capture.Body.Span))];
        bodies[0].Should().Contain("demo_channel/101").And.NotContain("demo_channel/102");
        read.Captures.Should().OnlyContain(capture => capture.ContentType == "text/html");
    }

    [Theory]
    [InlineData("https://t.me/s/demo_channel", true)]
    [InlineData("https://t.me/s/Demo_Channel/", true)]
    [InlineData("https://t.me/s/demo_channel?before=101", true)]
    [InlineData("https://t.me/demo_channel", false)]
    [InlineData("https://t.me/s/demo_channel?q=%23tag", false)]
    [InlineData("https://t.me/s/demo_channel?before=abc", false)]
    [InlineData("https://t.me/s/demo_channel?before=5&q=x", false)]
    [InlineData("https://t.me/s/demo_channel/101", false)]
    [InlineData("https://t.me/s/abc", false)]
    [InlineData("http://t.me/s/demo_channel", false)]
    [InlineData("https://t.me:8443/s/demo_channel", false)]
    [InlineData("https://telegram.me/s/demo_channel", false)]
    [InlineData("https://user@t.me/s/demo_channel", false)]
    [InlineData("https://t.me/s/demo_channel#top", false)]
    public void Only_a_channels_preview_page_is_a_supported_destination(string url, bool allowed)
    {
        Reader.Allows(new Uri(url)).Should().Be(allowed);
        Reader.CanRead(Document("<html></html>", url)).Should().Be(allowed);
    }

    [Fact]
    public async Task What_is_not_a_preview_page_fails_rather_than_reading_as_something_else()
    {
        string noHistory = "<html><body><div class=\"tgme_page\"><div class=\"tgme_page_title\">Demo Channel</div></div></body></html>";
        string challenge = "<html><title>Verify you are human</title><body>Checking your browser</body></html>";

        (await Reader.ReadAsync(Document(noHistory))).Should().BeOfType<Result<CaptureReadResult>.Failure>("the preview is off");
        (await Reader.ReadAsync(Document(challenge))).Should().BeOfType<Result<CaptureReadResult>.Failure>();
        (await Reader.ReadAsync(Document(Page(Message(101, "x")), contentType: "application/json")))
            .Should().BeOfType<Result<CaptureReadResult>.Failure>("a non-HTML answer is never parsed");
        (await Reader.ReadAsync(Document(Page(Message(101, "x")), "https://t.me/demo_channel")))
            .Should().BeOfType<Result<CaptureReadResult>.Failure>("a redirect off the preview is not read");
        (await Reader.ReadAsync(Document(Page(Message(101, "x")), contentType: null)))
            .Should().BeOfType<Result<CaptureReadResult>.Success>("an answer with no declared type is read as HTML");
    }

    private static async Task<CaptureReadResult> ReadAsync(string html, string url = "https://t.me/s/demo_channel")
    {
        var read = await Reader.ReadAsync(Document(html, url));
        return read.Should().BeOfType<Result<CaptureReadResult>.Success>().Subject.Value;
    }

    private static FetchedDocument Document(string html, string url = "https://t.me/s/demo_channel", string? contentType = "text/html; charset=utf-8") => new()
    {
        RequestUrl = new(url), FinalUrl = new(url), StatusCode = 200, ContentType = contentType,
        Body = Encoding.UTF8.GetBytes(html), FetchedAtUtc = new(2026, 11, 21, 8, 0, 0, TimeSpan.Zero),
    };

    private static string Page(params string[] messages) => $"""
        <!DOCTYPE html><html><head><title>Demo Channel – Telegram</title></head><body>
        <main class="tgme_main" data-url="/demo_channel"><div class="tgme_container">
        <section class="tgme_channel_history js-message_history">
        <div class="tgme_widget_message_centered js-messages_more_wrap"><a href="/s/demo_channel?before=1" class="tme_messages_more js-messages_more" data-before="1"></a></div>{string.Concat(messages)}
        </section></div></main></body></html>
        """;

    private static string Message(long id, string? text, string? time = "2026-11-18T09:30:00+00:00", int views = 10, string clock = "09:30",
        bool edited = false, string media = "", string extra = "", string user = Channel) =>
        MessageBlock($"{user}/{id}", text, time, views, clock, edited, media, extra, user, id);

    private static string MessageBlock(string post, string? text, string? time = "2026-11-18T09:30:00+00:00", int views = 10, string clock = "09:30",
        bool edited = false, string media = "", string extra = "", string user = Channel, long id = 1) => $"""
        <div class="tgme_widget_message_wrap js-widget_message_wrap"><div class="tgme_widget_message text_not_supported_wrap js-widget_message" data-post="{post}" data-view="opaque">
          <div class="tgme_widget_message_user"><a href="https://t.me/{user}"><i class="tgme_widget_message_user_photo bgcolor1" data-content="D"><img src="https://cdn4.telesco.pe/file/avatar.jpg"></i></a></div>
          <div class="tgme_widget_message_bubble">
            <div class="tgme_widget_message_author accent_color"><a class="tgme_widget_message_owner_name" href="https://t.me/{user}"><span dir="auto">Demo Channel</span></a></div>
        {media}{(text is null ? "" : $"<div class=\"tgme_widget_message_text js-message_text\" dir=\"auto\">{text}</div>")}{extra}
            <div class="tgme_widget_message_footer compact js-message_footer"><div class="tgme_widget_message_info short js-message_info">
              <span class="tgme_widget_message_views">{views}</span><span class="copyonly"> views</span><span class="tgme_widget_message_meta">{(edited ? "edited &nbsp;" : "")}<a class="tgme_widget_message_date" href="https://t.me/{user}/{id}">{(time is null ? "" : $"<time datetime=\"{time}\" class=\"time\">{clock}</time>")}</a></span>
            </div></div>
          </div>
        </div></div>
        """;
}
