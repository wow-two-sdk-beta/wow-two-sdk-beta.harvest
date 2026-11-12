using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Craftfair;

namespace HarvestAdapters.Tests.Unit.Sources.Craftfair;

/// <summary>Reduced Craftfair markup separates event dates from application status and countdowns.</summary>
public sealed class CraftfairCaptureReaderTests
{
    private const string Article = "https://craftfair.uz/events/craft-fair-2026?lang=en";

    [Fact]
    public async Task Read_ShouldUseTheEventSpan_AndPreserveTheSeparateClosedApplicationDeadline()
    {
        Capture capture = (await Read(Page())).Captures.Single();
        JsonElement node = capture.Structured.Single();
        JsonElement evidence = node.GetProperty("craftfair");

        node.GetProperty("startDate").GetString().Should().Be("2026-11-25");
        node.GetProperty("endDate").GetString().Should().Be("2026-11-27");
        evidence.GetProperty("applicationDeadline").GetString().Should().Be("2026-10-26T23:59:59+05:00");
        evidence.GetProperty("applicationStatusRaw").GetString().Should().Be("Applications are closed");
        evidence.GetProperty("participation").GetProperty("artisan").GetString().Should().Be("closed");
        evidence.GetProperty("participation").GetProperty("visitor").GetString().Should().Be("unknown");
        evidence.GetProperty("fees").GetString().Should().Be("unknown");
        capture.PublishedAtUtc.Should().BeNull();
        capture.ExternalKey.Should().Be("https://craftfair.uz/events/craft-fair-2026");
    }

    [Fact]
    public async Task Read_ShouldOmitPlaceholderProgrammeLinks_AndPreserveObservedExternalRegistrationLinks()
    {
        Capture capture = (await Read(Page())).Captures.Single();

        capture.Links.Should().ContainSingle();
        capture.Links.Single().Url.Should().Be("https://organizer.example/application?fair=2026");
        capture.Links.Should().NotContain(link => link.Text == "Programme");
    }

    [Theory]
    [InlineData("25 - 27 November")]
    [InlineData("25 November")]
    public async Task Read_ShouldNeverSupplyTheCountdownYearForYearlessEventDates(string rawDates)
    {
        Capture capture = (await Read(Page(rawDates))).Captures.Single();
        JsonElement node = capture.Structured.Single();

        node.TryGetProperty("startDate", out _).Should().BeFalse();
        node.TryGetProperty("endDate", out _).Should().BeFalse();
        node.GetProperty("craftfair").GetProperty("applicationDeadline").GetString().Should().Be("2026-10-26T23:59:59+05:00");
        capture.Warnings.Should().Contain("event_date_unknown");
    }

    [Fact]
    public async Task Read_ShouldFallBackToThePrintedDeadlineDay_WhenTheCountdownHasNoTimeZone()
    {
        string html = Page().Replace("2026-10-26T23:59:59+05:00", "2026-10-26T23:59:59", StringComparison.Ordinal);

        JsonElement node = (await Read(html)).Captures.Single().Structured.Single();

        node.GetProperty("craftfair").GetProperty("applicationDeadline").GetString().Should().Be("2026-10-26");
        node.GetProperty("startDate").GetString().Should().Be("2026-11-25");
    }

    [Fact]
    public async Task Read_ShouldDiscoverOnlyObservedEventCardsAndNextListingLinks()
    {
        string html =
            """
            <html><body>
            <h3 class="event-card-title"><a href="/events/craft-fair-2026?lang=en">Craft fair</a></h3>
            <h3 class="event-card-title"><a href="/events/craft-fair-2026?lang=en">Duplicate</a></h3>
            <h3 class="event-card-title"><a href="/events-preview">Unrelated route</a></h3>
            <h3 class="event-card-title"><a href="https://craftfair.uz.attacker.example/events/other">Other host</a></h3>
            <a rel="next" href="/events?lang=en&amp;page=2">Next</a>
            <a href="/events/undisplayed-card">Navigation only</a>
            <button>More</button>
            </body></html>
            """;

        CaptureReadResult result = await Read(html, "https://craftfair.uz/events?lang=en");

        result.Complete.Should().BeFalse();
        result.Captures.Should().BeEmpty();
        result.Discovered.Should().HaveCount(2);
        result.Discovered.Should().Contain(item => item.Url == Article && !item.IsListing);
        result.Discovered.Should().Contain(item => item.Url == "https://craftfair.uz/events?lang=en&page=2" && item.IsListing);
    }

    [Theory]
    [InlineData("https://craftfair.uz/events", "text/html", true)]
    [InlineData("https://craftfair.uz/events-preview", "text/html", false)]
    [InlineData("https://craftfair.uz.attacker.example/events", "text/html", false)]
    [InlineData("https://craftfair.uz/events", "application/xml", false)]
    public void CanRead_ShouldBindOnlyPublicEventHtml(string url, string contentType, bool expected)
    {
        new CraftfairCaptureReader().CanRead(Document("<html />", url, contentType)).Should().Be(expected);
    }

    private static async Task<CaptureReadResult> Read(string html, string url = Article)
    {
        Result<CaptureReadResult> result = await new CraftfairCaptureReader().ReadAsync(Document(html, url));
        return result is Result<CaptureReadResult>.Success success ? success.Value
            : throw new Xunit.Sdk.XunitException(((Result<CaptureReadResult>.Failure)result).Error.Message);
    }

    private static FetchedDocument Document(string html, string url, string contentType = "text/html") => new()
    {
        RequestUrl = new Uri(url), FinalUrl = new Uri(url), StatusCode = 200, ContentType = contentType,
        Body = Encoding.UTF8.GetBytes(html), FetchedAtUtc = new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero),
    };

    private static string Page(string eventDates = "25 - 27 November, 2026") => $"""
        <html lang="en"><body><h1 class="h1">Craft Fair 2026</h1>
        <article class="event-detail-panel"><div class="detail-rich-text"><p>Garments and artisan crafts.</p></div>
        <a class="btn-editorial-outline" href="#">Programme</a></article>
        <aside class="event-side-panel"><span class="status-badge">Applications are closed</span>
        <div class="countdown-minimal" data-deadline="2026-10-26T23:59:59+05:00"></div>
        <div class="sidebar-meta-item"><span class="meta-label">Event dates</span><span class="meta-val">{eventDates}</span></div>
        <div class="sidebar-meta-item"><span class="meta-label">Registration deadline</span><span class="meta-val highlight">26 October, 2026</span></div>
        <div class="sidebar-meta-item"><span class="meta-label">Location</span><span class="meta-val">Example expo centre</span></div>
        <a href="https://organizer.example/application?fair=2026">Registration information</a></aside>
        </body></html>
        """;
}
