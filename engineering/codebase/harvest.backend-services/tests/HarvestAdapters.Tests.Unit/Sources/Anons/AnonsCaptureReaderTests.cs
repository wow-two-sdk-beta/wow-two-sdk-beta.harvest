using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Anons;

namespace HarvestAdapters.Tests.Unit.Sources.Anons;

/// <summary>Reduced ANONS markup protects date meaning, embedded content and bounded discovery.</summary>
public sealed class AnonsCaptureReaderTests
{
    private const string Article = "https://anons.uz/ru/events/designer-market";

    [Fact]
    public async Task Read_ShouldSeparatePublicationFromEventSpan_AndReadTheEditorJsArticle()
    {
        string html = Page(new
        {
            name = "SEO ticket title", startDate = "2026-10-31T10:00:00+05:00", endDate = "2026-11-01T22:00:00+05:00",
        }, "Designer garments and local makers.");

        Capture capture = (await Read(html)).Captures.Single();

        capture.ExternalKey.Should().Be(Article);
        capture.PublishedAtUtc.Should().Be(new DateTimeOffset(2026, 10, 26, 15, 36, 0, TimeSpan.Zero));
        capture.Structured[0].GetProperty("name").GetString().Should().Be("Designer market");
        capture.Structured[0].GetProperty("description").GetString().Should().Be("Designer garments and local makers.");
        capture.Structured[0].GetProperty("startDate").GetString().Should().Be("2026-10-31T10:00:00+05:00");
        capture.Structured[0].GetProperty("endDate").GetString().Should().Be("2026-11-01T22:00:00+05:00");
    }

    [Fact]
    public async Task Read_ShouldKeepOnlyTheDay_WhenTheEventTimeDisagreesWithThePrintedTime()
    {
        // Live, 2026-11-17: the page's Event said 21:00+05:00 while its printed time said 16:00–18:00.
        string html = Page(new { startDate = "2026-11-17T21:00:00+05:00", endDate = "2026-11-17T23:00:00+05:00" }, "Book evening.")
            .Replace("10:00 - 22:00", "16:00–18:00", StringComparison.Ordinal);

        Capture capture = (await Read(html)).Captures.Single();
        JsonElement node = capture.Structured[0];

        node.GetProperty("startDate").GetString().Should().Be("2026-11-17");
        node.GetProperty("endDate").GetString().Should().Be("2026-11-17");
        node.GetProperty("anons").GetProperty("timeConflict").GetBoolean().Should().BeTrue();
        node.GetProperty("anons").GetProperty("rawTimes").GetString().Should().Be("16:00–18:00");
        capture.Warnings.Should().Equal("event_time_conflict");
    }

    [Theory]
    [InlineData("2026-10-31T10:00:00+05:00", "10:00 - 22:00")]
    [InlineData("2026-10-31T05:00:00Z", "10:00 - 22:00")]
    [InlineData("2026-10-31T09:30:00+05:00", "с 9.30")]
    public async Task Read_ShouldKeepTheEventTime_WhenThePrintedTimeAgreesInTashkent(string start, string printed)
    {
        string html = Page(new { startDate = start }, "Garment fair.").Replace("10:00 - 22:00", printed, StringComparison.Ordinal);

        Capture capture = (await Read(html)).Captures.Single();

        capture.Structured[0].GetProperty("startDate").GetString().Should().Be(start);
        capture.Structured[0].GetProperty("anons").GetProperty("timeConflict").GetBoolean().Should().BeFalse();
        capture.Warnings.Should().BeEmpty();
    }

    [Fact]
    public async Task Read_ShouldKeepRawFieldsAndFreeVisitorAdmission_WithoutInventingVendorAccess()
    {
        JsonElement node = (await Read(Page(new { startDate = "2026-10-31" }, "Handmade garments."))).Captures.Single().Structured[0];

        JsonElement evidence = node.GetProperty("anons");
        evidence.GetProperty("rawDates").GetString().Should().Be("31 Октября - 1 Ноября 2026");
        evidence.GetProperty("rawTimes").GetString().Should().Be("10:00 - 22:00");
        evidence.GetProperty("rawPrice").GetString().Should().Be("вход свободный");
        evidence.GetProperty("rawVenue").GetString().Should().Be("Example venue, Example street");
        evidence.GetProperty("visitorAdmission").GetString().Should().Be("free");
        evidence.GetProperty("participation").GetProperty("vendor").GetString().Should().Be("unknown");
        node.GetProperty("offers").GetProperty("price").GetString().Should().Be("0");
        node.GetProperty("isAccessibleForFree").GetBoolean().Should().BeTrue();
    }

    [Theory]
    [InlineData(null)]
    [InlineData("2026-10-31T10:00:00")]
    [InlineData("31 октября")]
    public async Task Read_ShouldLeaveDatesUnknown_WhenTheEventYearOrTimeZoneIsNotExplicit(string? date)
    {
        Capture capture = (await Read(Page(new { startDate = date }, "Show on 31 October."))).Captures.Single();

        capture.Structured[0].TryGetProperty("startDate", out _).Should().BeFalse();
        capture.Warnings.Should().Contain("event_date_unknown");
        capture.PublishedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Read_ShouldReadFreeVisitorEntry_WhenThePriceNamesVisitors()
    {
        // Live wording on trade-fair pages (2026-11-17); exhibitor and vendor terms stay unknown.
        string html = Page(new { startDate = "2026-11-19" }, "Trade fair.")
            .Replace("вход свободный", "Вход для посетителей бесплатный", StringComparison.Ordinal);

        JsonElement node = (await Read(html)).Captures.Single().Structured[0];

        node.GetProperty("anons").GetProperty("visitorAdmission").GetString().Should().Be("free");
        node.GetProperty("anons").GetProperty("participation").GetProperty("exhibitor").GetString().Should().Be("unknown");
        node.GetProperty("isAccessibleForFree").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Read_ShouldPreserveExplicitTicketMinimum_AndKeepSellerFeeUnknown()
    {
        string html = Page(new { startDate = "2026-11-21" }, "Fashion show.")
            .Replace("вход свободный", "от 200 000 сум", StringComparison.Ordinal);

        JsonElement node = (await Read(html)).Captures.Single().Structured[0];

        node.GetProperty("offers").GetProperty("lowPrice").GetString().Should().Be("200000");
        node.GetProperty("offers").GetProperty("priceCurrency").GetString().Should().Be("UZS");
        node.GetProperty("offers").TryGetProperty("price", out _).Should().BeFalse();
        node.GetProperty("anons").GetProperty("participation").GetProperty("vendor").GetString().Should().Be("unknown");
    }

    [Fact]
    public async Task Read_ShouldReadInlineRegistrationAndImageBlocks_WithoutFollowingScripts()
    {
        string html = Page(new { startDate = "2026-10-31" },
            "<a href='https://organizer.example/apply?event=7&amp;utm_source=anons'>Apply</a><script>untrusted()</script>",
            image: "/storage/garment.jpg");

        Capture capture = (await Read(html)).Captures.Single();

        capture.Links.Single().Url.Should().Be("https://organizer.example/apply?event=7");
        capture.Media.Should().Contain(media => media.Url == "https://anons.uz/storage/garment.jpg");
        capture.Structured[0].GetProperty("description").GetString().Should().NotContain("untrusted");
    }

    [Fact]
    public async Task Read_ShouldDiscoverOnlyObservedPublicEventAndCalendarTargets()
    {
        string html =
            """
            <html><body>
            <a href="/ru/events/designer-market?utm_source=home">Market</a>
            <a href="/ru/events/designer-market">Duplicate</a>
            <a href="/ru/events?date=2026-10-31#calendar">31 October</a>
            <a href="/ru/events?tag=43">Fairs</a>
            <section><h2 class="section-title">События</h2><div data-url="/ru/fragment/section/3"></div></section>
            <div data-url="/ru/fragment/section/7"></div>
            <a href="/ru/events">Current</a>
            <a href="/ru/news/fashion-reportage">Reportage</a>
            <a href="/ru/search?q=moda">Search</a>
            <a href="/ru/auth/registration">Account</a>
            <a href="https://anons.uz.attacker.example/ru/events/other">Other host</a>
            <button>More</button>
            </body></html>
            """;

        CaptureReadResult result = await Read(html, "https://anons.uz/ru/events");

        result.Complete.Should().BeFalse();
        result.Captures.Should().BeEmpty();
        result.Discovered.Should().HaveCount(3);
        result.Discovered[0].Should().Match<DiscoveredUrl>(item => item.Url == Article && !item.IsListing);
        result.Discovered.Should().Contain(item => item.Url == "https://anons.uz/ru/events?date=2026-10-31" && item.IsListing);
        result.Discovered.Should().Contain(item => item.Url == "https://anons.uz/ru/fragment/section/3" && item.IsListing);
        result.Discovered.Should().NotContain(item => item.Url.Contains("tag=", StringComparison.Ordinal));
        result.Discovered.Should().NotContain(item => item.Url.Contains("page=", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Read_ShouldReturnEventsBeforeListings_AndSkipFilterLocaleAndHomeLinks()
    {
        // Trimmed from the live events page (2026-11-17): navigation, a tag bar and a calendar strip precede the cards,
        // so following them in page order spent a 5-page run on listings and read no event.
        string html =
            """
            <html lang="ru"><body>
            <header><a href="https://anons.uz/ru">Home</a><a href="https://anons.uz/ru/events">События</a>
            <a href="https://anons.uz/uz/events">O'zbekcha</a></header>
            <section class="section" id="calendar">
            <div class="section-tags"><a class="tag" href="https://anons.uz/ru/events?tag=111">Выставки</a>
            <a class="tag" href="https://anons.uz/ru/events?tag=113">Мастер-классы</a></div>
            <div class="section-calendar calendar" data-url="https://anons.uz/ru/events">
            <a href="https://anons.uz/ru/events?date=2026-11-17#calendar">17</a>
            <a href="https://anons.uz/ru/events?date=2026-11-18#calendar">18</a></div>
            <h1 class="section-title">События</h1>
            <a href="https://anons.uz/ru/events?#calendar">Все</a><a href="https://anons.uz/ru/events?day=today#calendar">Сегодня</a>
            <a href="https://anons.uz/ru/events?day=weekend#calendar">Выходные</a>
            <article class="card"><a href="https://anons.uz/ru/events/book-evening-17-n">Book evening</a></article>
            <article class="card"><a href="https://anons.uz/ru/events/fashion-week-2026">Fashion week</a>
            <a href="https://anons.uz/ru/events/fashion-week-2026">Fashion week</a></article>
            <article class="card"><a href="https://anons.uz/uz/events/fashion-week-2026">Moda haftaligi</a></article>
            </section></body></html>
            """;

        CaptureReadResult result = await Read(html, "https://anons.uz/ru/events");

        result.Discovered.Select(item => item.Url).Should().Equal(
            "https://anons.uz/ru/events/book-evening-17-n",
            "https://anons.uz/ru/events/fashion-week-2026",
            "https://anons.uz/ru/events?date=2026-11-17",
            "https://anons.uz/ru/events?date=2026-11-18");
        result.Discovered.Take(2).Should().OnlyContain(item => !item.IsListing);
        result.Discovered.Skip(2).Should().OnlyContain(item => item.IsListing);
    }

    [Fact]
    public async Task Read_ShouldFollowTheLocaleEventsList_FromTheHomePage()
    {
        string html =
            """
            <html lang="ru"><body><a href="https://anons.uz/ru/events">События</a><a href="https://anons.uz/ru/events?tag=111">Выставки</a>
            <a href="https://anons.uz/ru/events/fashion-week-2026">Fashion week</a><a href="https://anons.uz/uz/events">O'zbekcha</a>
            </body></html>
            """;

        CaptureReadResult result = await Read(html, "https://anons.uz/ru");

        result.Discovered.Select(item => item.Url).Should().Equal(
            "https://anons.uz/ru/events/fashion-week-2026", "https://anons.uz/ru/events");
    }

    [Fact]
    public async Task Read_ShouldKeepProjectionStable_WhenViewCountsAndEditorSaveTimesChange()
    {
        string html = Page(new { startDate = "2026-10-31" }, "Garment fair.");
        Capture first = (await Read(html)).Captures.Single();
        Capture second = (await Read(html.Replace("views'>85", "views'>300", StringComparison.Ordinal)
            .Replace("1793108512332", "1793194912332", StringComparison.Ordinal))).Captures.Single();

        first.ContentProjection.Should().Be(second.ContentProjection);
    }

    [Fact]
    public async Task Read_ShouldNoticeMediaOnlyChangesInTheProjection()
    {
        object data = new { startDate = "2026-10-31" };
        Capture first = (await Read(Page(data, "Garment fair.", "/storage/one.jpg"))).Captures.Single();
        Capture second = (await Read(Page(data, "Garment fair.", "/storage/two.jpg"))).Captures.Single();

        first.ContentProjection.Should().NotBe(second.ContentProjection);
    }

    [Fact]
    public async Task Read_ShouldIgnoreCrossHostCanonicalAndKeepVisibleFallbackWhenEditorJsonIsMalformed()
    {
        string html =
            """
            <html lang="ru"><head><link rel="canonical" href="https://attacker.example/event"></head><body>
            <div class="section-container"><h1>Designer market</h1><div class="section-content content">
            <div data-editorjs="not-json"><p>Local garment designers.</p></div>
            </div></div></body></html>
            """;

        Capture capture = (await Read(html)).Captures.Single();

        capture.ExternalKey.Should().Be(Article);
        capture.Structured[0].GetProperty("description").GetString().Should().Be("Local garment designers.");
        capture.Warnings.Should().Contain("event_date_unknown");
    }

    [Theory]
    [InlineData("https://anons.uz/ru/events", "text/html", true)]
    [InlineData("https://anons.uz/ru/fragment/section/3", "text/html", true)]
    [InlineData("https://anons.uz/ru/search", "text/html", false)]
    [InlineData("https://anons.uz.attacker.example/ru/events", "text/html", false)]
    [InlineData("https://anons.uz/ru/events", "application/xml", false)]
    public void CanRead_ShouldBindOnlyItsKnownPublicHtmlRoutes(string url, string type, bool expected)
    {
        new AnonsCaptureReader().CanRead(Document("<html />", url, type)).Should().Be(expected);
    }

    [Fact]
    public async Task Read_ShouldRespectCancellation()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Func<Task> read = () => new AnonsCaptureReader().ReadAsync(Document("<html />", Article), cancelled.Token);

        await read.Should().ThrowAsync<OperationCanceledException>();
    }

    private static async Task<CaptureReadResult> Read(string html, string url = Article)
    {
        Result<CaptureReadResult> result = await new AnonsCaptureReader().ReadAsync(Document(html, url));
        return result is Result<CaptureReadResult>.Success success ? success.Value
            : throw new Xunit.Sdk.XunitException(((Result<CaptureReadResult>.Failure)result).Error.Message);
    }

    private static FetchedDocument Document(string html, string url, string type = "text/html") => new()
    {
        RequestUrl = new Uri(url), FinalUrl = new Uri(url), StatusCode = 200, ContentType = type,
        Body = Encoding.UTF8.GetBytes(html), FetchedAtUtc = new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero),
    };

    private static string Page(object original, string paragraph, string? image = null)
    {
        var node = new Dictionary<string, object?> { ["@type"] = "Event" };
        foreach (JsonProperty property in JsonSerializer.SerializeToElement(original).EnumerateObject())
            node[property.Name] = property.Value.Clone();
        string graph = JsonSerializer.Serialize(new Dictionary<string, object?>
        {
            ["@graph"] = new object[] { new Dictionary<string, object?> { ["@type"] = "WebPage" }, node },
        });
        var blocks = new List<object> { new { type = "paragraph", data = new { text = paragraph } } };
        if (image is not null)
            blocks.Add(new { type = "image", data = new { file = new { url = image } } });
        string editor = WebUtility.HtmlEncode(JsonSerializer.Serialize(new { time = 1793108512332L, blocks }));
        return $"""
            <html lang="ru"><head><link rel="canonical" href="{Article}">
            <meta property="article:published_time" content="2026-10-26T20:36:00+05:00">
            <script type="application/ld+json">{graph}</script></head><body>
            <div class="section-container"><h1>Designer market</h1><span class='section-views'>85</span>
            <div class="section-content content"><div class="editorjs-content" data-editorjs="{editor}"></div></div>
            <div class="section-more__content"><b>Дата проведения:</b><span>31 Октября - 1 Ноября 2026</span></div>
            <div class="section-more__content"><b>Время проведения:</b><span>10:00 - 22:00</span></div>
            <div class="section-more__content"><b>Стоимость:</b><span>вход свободный</span></div>
            <div class="section-more__content"><b>Локация:</b><span>Example venue, Example street</span></div>
            </div></body></html>
            """;
    }
}
