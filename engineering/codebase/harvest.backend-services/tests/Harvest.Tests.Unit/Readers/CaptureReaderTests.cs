using System.Text;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;

namespace Harvest.Tests.Unit.Readers;

/// <summary>Exercises format fixtures without fetching the named fixture hosts.</summary>
public sealed class CaptureReaderTests
{
    [Theory]
    [InlineData("#event", "https://example.com/first#event")]
    [InlineData("_:event", "https://example.com/first#blank-event")]
    public async Task JsonLd_ShouldScopeRelativeAndBlankNodeIdentitiesToTheirPage(string id, string expected)
    {
        var reader = new JsonLdEventReader();
        var json = System.Text.Json.JsonSerializer.Serialize(new Dictionary<string, object> { ["@type"] = "Event", ["@id"] = id, ["name"] = "Show" });
        var first = await reader.ReadAsync(Document(json, "https://example.com/first", "application/ld+json"));
        var second = await reader.ReadAsync(Document(json, "https://example.com/second", "application/ld+json"));
        first.Data().Captures[0].ExternalKey.Should().Be(expected);
        first.Data().Captures[0].ExternalKey.Should().NotBe(second.Data().Captures[0].ExternalKey);
        if (id.StartsWith("_:", StringComparison.Ordinal)) first.Data().Captures[0].OriginKey.Should().BeNull();
    }

    [Fact]
    public async Task JsonLd_ShouldReadAnEventFromGraphAndIgnoreMalformedSiblingScript()
    {
        var document = Document("""
            <html lang="uz"><head><script type="application/ld+json">{broken}</script>
            <script type="application/ld+json">{"@graph":[{"@type":"Organization","name":"Mstar"},
              {"@type":["Event","MusicEvent"],"@id":"https://mstar.uz/events/concert", "name":"Toshkent konserti",
              "description":"Jonli musiqa", "startDate":"2026-11-03T19:00:00+05:00"}]}</script></head></html>
            """, "https://mstar.uz/events/concert");
        var reader = new JsonLdEventReader();
        reader.CanRead(document).Should().BeTrue();
        var result = await reader.ReadAsync(document);
        result.IsSuccess.Should().BeTrue();
        result.Data().Captures.Should().ContainSingle();
        var capture = result.Data().Captures[0];
        capture.ExternalKey.Should().Be("https://mstar.uz/events/concert");
        capture.Locale.Should().Be("uz");
        capture.Warnings.Should().Contain("malformed_jsonld");
        capture.Structured[0].GetProperty("name").GetString().Should().Be("Toshkent konserti");
    }

    [Fact]
    public async Task JsonLd_ShouldKeepProjectionStableWhenPropertyOrderAndMetricsChange()
    {
        var reader = new JsonLdEventReader();
        var first = await reader.ReadAsync(Document("""{"@type":"Event","name":"Show","startDate":"2026-11-01","interactionStatistic":{"userInteractionCount":1}}""", contentType: "application/ld+json"));
        var second = await reader.ReadAsync(Document("""{"interactionStatistic":{"userInteractionCount":999},"startDate":"2026-11-01","name":"Show","@type":"Event"}""", contentType: "application/ld+json"));
        first.Data().Captures[0].ContentProjection.Should().Be(second.Data().Captures[0].ContentProjection);
    }

    [Fact]
    public async Task JsonLd_ShouldKeepAnonymousEventIdentitiesStableWhenArrayOrderChanges()
    {
        var reader = new JsonLdEventReader();
        const string one = """{"@type":"Event","name":"One","startDate":"2026-11-01"}""";
        const string two = """{"@type":"Event","name":"Two","startDate":"2026-11-02"}""";
        var first = await reader.ReadAsync(Document($"[{one},{two}]", contentType: "application/ld+json"));
        var second = await reader.ReadAsync(Document($"[{two},{one}]", contentType: "application/ld+json"));
        first.Data().Captures.Select(capture => capture.ExternalKey).Should().BeEquivalentTo(second.Data().Captures.Select(capture => capture.ExternalKey));
        first.Data().Captures.Select(capture => capture.ExternalKey).Should().OnlyHaveUniqueItems();
    }

    [Fact]
    public async Task Sitemap_ShouldReadNamespacedIndexAndRetainChildListingFlag()
    {
        var result = await new SitemapCaptureReader().ReadAsync(Document("""
            <sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9"><sitemap>
              <loc>https://voyage.uz/events-sitemap.xml</loc><lastmod>2026-10-27T10:00:00Z</lastmod>
            </sitemap></sitemapindex>
            """, "https://voyage.uz/sitemap.xml", "application/xml"));
        result.IsSuccess.Should().BeTrue();
        result.Data().Captures.Should().BeEmpty();
        result.Data().Discovered.Should().ContainSingle();
        result.Data().Discovered[0].IsListing.Should().BeTrue();
        result.Data().Complete.Should().BeTrue();
    }

    [Fact]
    public async Task Sitemap_ShouldRefuseExternalEntitiesWithoutReadingThem()
    {
        var result = await new SitemapCaptureReader().ReadAsync(Document("""
            <!DOCTYPE urlset [<!ENTITY data SYSTEM "file:///etc/passwd">]><urlset><url><loc>&data;</loc></url></urlset>
            """, contentType: "application/xml"));
        result.IsSuccess.Should().BeFalse();
    }

    [Fact]
    public async Task Rss_ShouldCaptureEachGuidWithoutTreatingTheFeedAsComplete()
    {
        var result = await new FeedCaptureReader().ReadAsync(Document("""
            <rss version="2.0"><channel><language>ru</language><item><guid>post-17</guid><link>https://voyage.uz/event/17</link>
            <title>Выставка</title><description>&lt;p&gt;Вход бесплатный&lt;/p&gt;</description><pubDate>Tue, 27 Oct 2026 10:00:00 GMT</pubDate>
            <enclosure url="https://voyage.uz/a.jpg" type="image/jpeg"/></item></channel></rss>
            """, "https://voyage.uz/feed.xml", "application/rss+xml"));
        result.IsSuccess.Should().BeTrue();
        result.Data().Captures[0].ExternalKey.Should().Be("post-17");
        result.Data().Captures[0].Structured[0].GetProperty("text").GetString().Should().Be("Вход бесплатный");
        result.Data().Captures[0].Media.Should().ContainSingle();
        result.Data().Complete.Should().BeFalse();
    }

    [Fact]
    public async Task Atom_ShouldReadTheAlternateLinkAndHtmlContent()
    {
        var result = await new FeedCaptureReader().ReadAsync(Document("""
            <feed xmlns="http://www.w3.org/2005/Atom"><entry><id>urn:event:12</id><title>Show</title>
            <link rel="self" href="https://voyage.uz/api/12"/><link rel="alternate" href="/events/12"/>
            <author><name>Alice</name></author><content type="html">&lt;b&gt;Two&lt;/b&gt; words</content></entry></feed>
            """, "https://voyage.uz/feed", "application/atom+xml"));
        result.Data().Captures[0].Url.Should().Be("https://voyage.uz/events/12");
        result.Data().Captures[0].ExternalKey.Should().Be("urn:event:12");
        result.Data().Captures[0].Structured[0].GetProperty("author").GetString().Should().Be("Alice");
    }

    [Fact]
    public async Task Ics_ShouldUnfoldAndPreserveDatePrecisionAndZone()
    {
        var result = await new IcsCaptureReader().ReadAsync(Document("BEGIN:VCALENDAR\r\nBEGIN:VEVENT\r\nUID:show-12\r\nSUMMARY:Very long\r\n  event\r\nDESCRIPTION:Hello\\, world\\nNext line\r\nDTSTART;VALUE=DATE:20261103\r\nDTEND;VALUE=DATE:20261104\r\nURL:https://voyage.uz/event/12\r\nEND:VEVENT\r\nEND:VCALENDAR", contentType: "text/calendar"));
        result.IsSuccess.Should().BeTrue();
        var node = result.Data().Captures[0].Structured[0];
        node.GetProperty("name").GetString().Should().Be("Very long event");
        node.GetProperty("startDate").GetString().Should().Be("2026-11-03");
        node.GetProperty("description").GetString().Should().Be("Hello, world\nNext line");
    }

    [Fact]
    public async Task Ics_ShouldKeepFloatingLocalTimeAndRecurrenceExplicit()
    {
        var result = await new IcsCaptureReader().ReadAsync(Document("BEGIN:VCALENDAR\nBEGIN:VEVENT\nUID:weekly\nDTSTART;TZID=Asia/Tashkent:20261103T190000\nRRULE:FREQ=WEEKLY\nEND:VEVENT\nEND:VCALENDAR", contentType: "text/calendar"));
        var capture = result.Data().Captures[0];
        capture.Structured[0].GetProperty("startDate").GetString().Should().Be("2026-11-03T19:00:00");
        capture.Structured[0].GetProperty("timeZone").GetString().Should().Be("Asia/Tashkent");
        capture.Warnings.Should().Contain("recurrence_master_only");
    }

    [Fact]
    public async Task Page_ShouldCaptureTextLinksMediaAndContactMetadataWithoutScripts()
    {
        var result = await new PageCaptureReader().ReadAsync(Document("""
            <html lang="en"><head><title>Fallback</title><meta property="og:title" content="Concert"/>
            <link rel="canonical" href="/event"/></head><body><nav>Noise</nav><main><h1>Concert</h1><p>Two paragraphs.</p>
            <p>More text.</p><script>secret()</script><a href="/tickets?utm_source=feed&amp;id=4">Tickets</a>
            <a href="javascript:alert(1)">Bad</a><a href="tel:+998901234567">Phone</a><img src="/poster.jpg"/></main></body></html>
            """, "https://voyage.uz/event?utm_source=x"));
        var capture = result.Data().Captures[0];
        capture.Url.Should().Be("https://voyage.uz/event");
        capture.ContentProjection.Should().NotContain("secret").And.NotContain("Noise");
        capture.Structured[0].GetProperty("text").GetString().Should().Contain("paragraphs. More");
        capture.Links.Should().ContainSingle();
        capture.Links[0].Url.Should().Be("https://voyage.uz/tickets?id=4");
        capture.Media[0].Url.Should().Be("https://voyage.uz/poster.jpg");
        capture.Structured[0].GetProperty("contacts")[0].GetString().Should().Be("+998901234567");
    }

    [Fact]
    public void Registration_ShouldPreferStructuredReadersOverPlainHtml()
    {
        var services = new ServiceCollection().AddHarvestReaders().BuildServiceProvider();
        var readers = services.GetServices<ICaptureReader>().ToList();
        var document = Document("""<script type="application/ld+json">{"@type":"Event","name":"Show"}</script>""");
        readers.First(reader => reader.CanRead(document)).Key.Should().Be("jsonld-event");
    }

    internal static FetchedDocument Document(string body, string url = "https://example.com/page", string contentType = "text/html") => new()
    {
        RequestUrl = new(url), FinalUrl = new(url), StatusCode = 200, ContentType = contentType,
        Body = Encoding.UTF8.GetBytes(body), FetchedAtUtc = new(2026, 10, 27, 10, 0, 0, TimeSpan.Zero),
    };
}
