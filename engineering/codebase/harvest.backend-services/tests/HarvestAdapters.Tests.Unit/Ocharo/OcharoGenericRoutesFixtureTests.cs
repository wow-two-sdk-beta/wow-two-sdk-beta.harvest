using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;

namespace HarvestAdapters.Tests.Unit.Ocharo;

/// <summary>Reads one trimmed, synthetic fixture per Wave 1 route family through the generic readers. Each fixture is
/// shaped like a body probed on 2026-11-21 with every name, host and text replaced; a reader is picked as the generic
/// step broker picks it, the first registered one that recognizes the response. A fixture parse is no live access.</summary>
public sealed class OcharoGenericRoutesFixtureTests
{
    private static readonly IReadOnlyList<ICaptureReader> Registered = new ServiceCollection().AddHarvestReaders()
        .BuildServiceProvider().GetServices<ICaptureReader>().ToList();

    [Fact]
    public async Task The_feed_fixture_reads_dated_items_through_the_feed_reader()
    {
        var document = Fixture("wave1-feed.xml", "https://fair.example.test/feed/", "application/rss+xml; charset=UTF-8", "UTF-8");

        var reader = Pick(document);
        var read = Value(await reader.ReadAsync(document));

        reader.Key.Should().Be("feed");
        read.Complete.Should().BeFalse("a feed is a moving window");
        read.Captures.Should().HaveCount(2).And.OnlyContain(capture => capture.PublishedAtUtc != null);
        var first = read.Captures[0];
        first.ExternalKey.Should().Be("https://fair.example.test/?p=101");
        first.Url.Should().Be("https://fair.example.test/2026/11/18/exhibitor-applications-open/");
        first.PublishedAtUtc.Should().Be(new DateTimeOffset(2026, 11, 18, 8, 30, 0, TimeSpan.Zero));
        first.Structured[0].GetProperty("title").GetString().Should().Be("Exhibitor applications open for the autumn fair");
        first.Structured[0].GetProperty("text").GetString().Should().Contain("open until 20 December");
    }

    [Fact]
    public async Task The_calendar_fixture_reads_dated_events_through_the_ics_reader()
    {
        var document = Fixture("wave1-calendar.ics", "https://events.example.test/cal/c/festival.ics", "text/calendar; charset=utf-8", "utf-8");

        var reader = Pick(document);
        var read = Value(await reader.ReadAsync(document));

        reader.Key.Should().Be("ics");
        read.Captures.Should().HaveCount(2);
        read.Captures.Select(capture => capture.ExternalKey).Should().OnlyHaveUniqueItems();
        var first = read.Captures[0];
        first.Url.Should().Be("https://events.example.test/e/example-textile-festival/");
        first.PublishedAtUtc.Should().Be(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero));
        first.Structured[0].GetProperty("startDate").GetString().Should().Be("2026-11-25T12:00:00Z");
        first.Structured[0].GetProperty("name").GetString().Should().Be("Example Textile Festival");
    }

    [Fact]
    public async Task The_structured_fixture_reads_the_dated_events_on_a_page_through_the_json_ld_reader()
    {
        var document = Fixture("wave1-structured.html", "https://expo.example.test/calendar", "text/html; charset=utf-8", "utf-8");

        var reader = Pick(document);
        var read = Value(await reader.ReadAsync(document));

        reader.Key.Should().Be("jsonld-event");
        read.Captures.Should().HaveCount(2, "the breadcrumb node is no event");
        read.Captures.Select(capture => capture.ExternalKey).Should().OnlyHaveUniqueItems();
        read.Captures.Select(capture => capture.Structured[0].GetProperty("startDate").GetString())
            .Should().Equal("2026-11-25", "2026-11-26");
        read.Captures.Select(capture => capture.Structured[0].GetProperty("name").GetString())
            .Should().Equal("Example Textile Expo", "Example Jewelry Fair");
        read.Captures.Should().OnlyContain(capture => capture.Locale == "en");
    }

    [Fact]
    public async Task The_site_fixtures_read_a_sitemap_into_dated_addresses_and_a_page_into_a_dated_capture()
    {
        var sitemap = Fixture("wave1-sitemap.xml", "https://expo.example.test/sitemap.xml", "application/xml", "utf-8");
        var page = Fixture("wave1-article.html", "https://expo.example.test/news/registration-opens", "text/html; charset=utf-8", "utf-8");

        var sitemapReader = Pick(sitemap);
        var pageReader = Pick(page);
        var listed = Value(await sitemapReader.ReadAsync(sitemap));
        var article = Value(await pageReader.ReadAsync(page));

        sitemapReader.Key.Should().Be("sitemap");
        listed.Captures.Should().BeEmpty("a sitemap's addresses are a frontier, never posts");
        listed.Complete.Should().BeTrue();
        listed.Discovered.Select(found => found.Url).Should().Equal("https://expo.example.test/news/registration-opens",
            "https://expo.example.test/news/runway-programme", "https://expo.example.test/about");
        listed.Discovered.Should().OnlyContain(found => !found.IsListing);
        DateTimeOffset?[] modified = [new DateTimeOffset(2026, 11, 17, 4, 0, 0, TimeSpan.Zero), new DateTimeOffset(2026, 11, 12, 0, 0, 0, TimeSpan.Zero), null];
        listed.Discovered.Select(found => found.LastModifiedUtc).Should().Equal(modified);

        pageReader.Key.Should().Be("page");
        var capture = article.Captures.Should().ContainSingle().Subject;
        capture.Url.Should().Be("https://expo.example.test/news/registration-opens");
        capture.PublishedAtUtc.Should().Be(new DateTimeOffset(2026, 11, 17, 4, 0, 0, TimeSpan.Zero));
        capture.Warnings.Should().BeEmpty();
        capture.Structured[0].GetProperty("title").GetString().Should().Be("Registration opens for Example Textile Expo");
        capture.Structured[0].GetProperty("contacts").GetArrayLength().Should().Be(0);
    }

    [Fact]
    public async Task A_sitemap_index_lists_child_sitemaps_as_listings_the_site_route_enumerates_next()
    {
        const string index = """
            <?xml version="1.0" encoding="UTF-8"?>
            <sitemapindex xmlns="http://www.sitemaps.org/schemas/sitemap/0.9">
              <sitemap><loc>https://expo.example.test/wp-sitemap-posts-post-1.xml</loc><lastmod>2026-11-17</lastmod></sitemap>
            </sitemapindex>
            """;
        var document = Document(index, "https://expo.example.test/wp-sitemap.xml", "application/xml");

        var reader = Pick(document);
        var read = Value(await reader.ReadAsync(document));

        reader.Key.Should().Be("sitemap");
        read.Discovered.Should().ContainSingle().Which.IsListing.Should().BeTrue();
    }

    [Fact]
    public async Task A_windows_1251_feed_keeps_its_cyrillic_text_when_the_response_names_the_charset()
    {
        var document = Fixture("wave1-feed-windows-1251.xml", "https://www.example.test/rss.cgi?lan=r", "text/xml; charset=windows-1251", "windows-1251");

        var read = Value(await Pick(document).ReadAsync(document));

        read.Captures.Should().HaveCount(2).And.OnlyContain(capture => capture.PublishedAtUtc != null);
        read.Captures[0].Structured[0].GetProperty("title").GetString().Should().Be("Неделя моды открывает показы коллекций");
    }

    private static ICaptureReader Pick(FetchedDocument document) => Registered.First(reader => reader.CanRead(document));

    private static FetchedDocument Fixture(string name, string url, string contentType, string charset) => new()
    {
        RequestUrl = new(url), FinalUrl = new(url), StatusCode = 200, ContentType = contentType, Charset = charset,
        Body = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Ocharo", "Fixtures", name)),
        FetchedAtUtc = new(2026, 11, 21, 8, 0, 0, TimeSpan.Zero),
    };

    private static FetchedDocument Document(string body, string url, string contentType) => new()
    {
        RequestUrl = new(url), FinalUrl = new(url), StatusCode = 200, ContentType = contentType,
        Body = System.Text.Encoding.UTF8.GetBytes(body), FetchedAtUtc = new(2026, 11, 21, 8, 0, 0, TimeSpan.Zero),
    };

    private static CaptureReadResult Value(Result<CaptureReadResult> result) => result is Result<CaptureReadResult>.Success success
        ? success.Value : throw new Xunit.Sdk.XunitException(((Result<CaptureReadResult>.Failure)result).Error.Message);
}
