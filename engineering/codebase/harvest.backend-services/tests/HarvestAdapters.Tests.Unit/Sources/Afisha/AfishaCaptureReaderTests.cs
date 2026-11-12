using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Afisha;

namespace HarvestAdapters.Tests.Unit.Sources.Afisha;

/// <summary>Reduced source-shape fixtures pin discovery, source evidence and event-date separation.</summary>
public sealed class AfishaCaptureReaderTests
{
    private const string Article = "https://www.afisha.uz/ru/fashion/2026/10/14/fashline-26";

    [Fact]
    public async Task Read_ShouldUseEventStartRatherThanPublicationDate_AndCleanTheSeoTitle()
    {
        string html = Page(
            """
            <script type="application/ld+json">
            {"@type":"Event","name":"SEO ticket boilerplate","startDate":"2026-11-21T17:00:00+05:00",
             "location":{"@type":"Place","name":"Fixture venue"}}
            </script>
            """,
            new
            {
                title = "Designers Showcase", type = "EVENT", locale = "ru", publishedAt = "2026-10-14T16:27:28+05:00",
                content = "<p>National garments and designer collections.</p>",
                eventSchedules = new[] { Schedule("2026-11-21T17:00:00+05:00", exact: true, minimum: "200000") },
            });

        Capture capture = (await Read(html)).Captures.Should().ContainSingle().Subject;

        capture.PublishedAtUtc.Should().Be(new DateTimeOffset(2026, 10, 14, 11, 27, 28, TimeSpan.Zero));
        JsonElement node = capture.Structured[0];
        node.GetProperty("name").GetString().Should().Be("Designers Showcase");
        node.GetProperty("startDate").GetString().Should().Be("2026-11-21T17:00:00+05:00");
        capture.ExternalKey.Should().Be(Article);
        capture.Warnings.Should().NotContain("event_date_unknown");
    }

    [Fact]
    public async Task Read_ShouldCompleteThePagePlaceWithTheScheduleCity_WhenItsAddressIsOneString()
    {
        string html = Page(
            """
            <script type="application/ld+json">
            {"@type":"Event","name":"SEO title","startDate":"2026-11-21T17:00:00+05:00",
             "location":{"@type":"Place","name":"Fixture venue","address":"Ташкент, Example street",
              "geo":{"@type":"GeoCoordinates","latitude":"41.3153","longitude":"69.2727"}}}
            </script>
            """,
            new
            {
                title = "Designers Showcase", type = "EVENT", content = "<p>Designer collections.</p>",
                eventSchedules = new[] { Schedule("2026-11-21T17:00:00+05:00", exact: true) },
            });

        JsonElement location = (await Read(html)).Captures.Single().Structured[0].GetProperty("location");

        location.GetProperty("name").GetString().Should().Be("Fixture venue");
        location.GetProperty("geo").GetProperty("latitude").GetString().Should().Be("41.3153");
        JsonElement address = location.GetProperty("address");
        address.GetProperty("addressLocality").GetString().Should().Be("Ташкент");
        address.GetProperty("streetAddress").GetString().Should().Be("Example street");
    }

    [Fact]
    public async Task Read_ShouldKeepThePagePlace_WhenTheScheduleNamesAnotherVenue()
    {
        string html = Page(
            """
            <script type="application/ld+json">
            {"@type":"Event","name":"SEO title","startDate":"2026-11-21T17:00:00+05:00",
             "location":{"@type":"Place","name":"Other hall","address":"Самарканд, Registan"}}
            </script>
            """,
            new
            {
                title = "Designers Showcase", type = "EVENT", content = "<p>Designer collections.</p>",
                eventSchedules = new[] { Schedule("2026-11-21T17:00:00+05:00", exact: true) },
            });

        JsonElement location = (await Read(html)).Captures.Single().Structured[0].GetProperty("location");

        location.GetProperty("address").GetString().Should().Be("Самарканд, Registan");
    }

    [Fact]
    public async Task Read_ShouldPreserveThePublishedExhibitionSpan_RatherThanDailyRemainingTickets()
    {
        string html = Page(
            """
            <script type="application/ld+json">
            {"@type":"Event","name":"SEO exhibition title","startDate":"2026-10-06","endDate":"2026-10-31"}
            </script>
            <section data-schedule="true"><button>27 октября</button><button>28 октября</button></section>
            """,
            new
            {
                title = "Textile exhibition", type = "EVENT", publishedAt = "2026-09-17T11:05:00+05:00",
                content = "<p>Historic clothing and textile objects.</p>",
                eventSchedules = new[]
                {
                    Schedule("2026-09-17T10:00:00+05:00", exact: true),
                    Schedule("2026-10-06T00:00:00+05:00", exact: false, end: "2026-10-31T00:00:00+05:00"),
                },
            });
        JsonElement node = (await Read(html)).Captures.Single().Structured[0];

        node.GetProperty("startDate").GetString().Should().Be("2026-10-06");
        node.GetProperty("endDate").GetString().Should().Be("2026-10-31");
        node.GetProperty("afisha").GetProperty("schedules").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Read_ShouldUseDayPrecision_WhenTheSourceSaysItsTimeIsNotExact()
    {
        string html = Page("", new
        {
            title = "Maker fair", type = "EVENT", content = "<p>Designer clothes.</p>",
            eventSchedules = new[] { Schedule("2026-11-01T00:00:00+05:00", exact: false) },
        });

        JsonElement node = (await Read(html)).Captures.Single().Structured[0];

        node.GetProperty("startDate").GetString().Should().Be("2026-11-01");
        node.GetProperty("location").GetProperty("@type").GetString().Should().Be("Place");
    }

    [Fact]
    public async Task Read_ShouldKeepEventDatesUnknown_WhenOnlyTheArticleDateAndYearlessTextExist()
    {
        string html = Page("", new
        {
            title = "Fashion meeting", type = "EVENT", publishedAt = "2026-10-14T16:27:28+05:00",
            content = "<p>Показ состоится 21 ноября.</p>",
        });

        Capture capture = (await Read(html)).Captures.Single();

        capture.Structured[0].TryGetProperty("startDate", out _).Should().BeFalse();
        capture.Warnings.Should().Contain("event_date_unknown");
        capture.PublishedAtUtc.Should().NotBeNull();
    }

    [Fact]
    public async Task Read_ShouldKeepEditorialAndPastReportageAsArticles()
    {
        string html = Page("", new
        {
            title = "Как прошла выставка дизайнеров", type = "ARTICLE", publishedAt = "2026-10-14T16:27:28+05:00",
            content = "<p>Обзор завершившейся выставки одежды.</p>",
        });

        Capture capture = (await Read(html)).Captures.Single();

        capture.Structured.Should().ContainSingle();
        capture.Structured[0].GetProperty("reader").GetString().Should().Be("page");
        capture.Structured[0].GetProperty("afisha").GetProperty("recordKind").GetString().Should().Be("article");
        capture.Warnings.Should().Contain("editorial_or_reportage");
    }

    [Fact]
    public async Task Read_ShouldKeepApplicationDeadlineSeparateFromTheUnknownEventDate()
    {
        string html = Page("", new
        {
            title = "Открыт прием заявок на конкурс дизайнеров", type = "ARTICLE", publishedAt = "2026-10-14T16:27:28+05:00",
            content = "<p>Прием заявок до 11.11.2026. Одежда и авторские коллекции.</p><a href='https://organizer.example/apply'>Заявка</a>",
        });

        Capture capture = (await Read(html)).Captures.Single();
        JsonElement node = capture.Structured[0];

        node.GetProperty("afisha").GetProperty("recordKind").GetString().Should().Be("call");
        node.GetProperty("afisha").GetProperty("applicationDeadlineEvidence").GetProperty("date").GetString().Should().Be("2026-11-11");
        node.TryGetProperty("startDate", out _).Should().BeFalse();
        capture.Links.Should().Contain(link => link.Url == "https://organizer.example/apply");
        node.GetProperty("afisha").GetProperty("participation").GetProperty("runway").GetString().Should().Be("unknown");
    }

    [Fact]
    public async Task Read_ShouldRetainYearlessDeadlineEvidenceWithoutGuessingThePublicationYear()
    {
        string html = Page("", new
        {
            title = "Открыт прием заявок на конкурс моды", type = "ARTICLE", publishedAt = "2026-10-14T16:27:28+05:00",
            content = "<p>Заявки принимаются до 11 ноября.</p>",
        });
        JsonElement evidence = (await Read(html)).Captures.Single().Structured[0].GetProperty("afisha");

        evidence.GetProperty("applicationDeadlineEvidence").GetProperty("date").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Read_ShouldNotMistakeAnEventDateInApplicationTextForADeadline()
    {
        string html = Page("", new
        {
            title = "Открыт прием заявок на конкурс моды", type = "ARTICLE",
            content = "<p>Заявки на показ, который состоится 21.11.2026, принимаются онлайн.</p>",
        });
        JsonElement evidence = (await Read(html)).Captures.Single().Structured[0].GetProperty("afisha");

        evidence.GetProperty("applicationDeadlineEvidence").GetProperty("date").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Read_ShouldKeepPriceMinimumAndParticipationUnknown_WithoutTreatingATicketAsASellerFee()
    {
        string html = Page("", new
        {
            title = "Fashion runway", type = "EVENT", content = "<p>Стоимость билетов: от 200 000 сумов.</p>",
            eventSchedules = new[] { Schedule("2026-11-21T17:00:00+05:00", exact: true, minimum: "200000") },
        });
        JsonElement node = (await Read(html)).Captures.Single().Structured[0];

        node.GetProperty("offers").GetProperty("lowPrice").GetString().Should().Be("200000");
        node.GetProperty("offers").GetProperty("priceCurrency").GetString().Should().Be("UZS");
        node.GetProperty("offers").TryGetProperty("price", out _).Should().BeFalse();
        node.GetProperty("afisha").GetProperty("participation").GetProperty("vendor").GetString().Should().Be("unknown");
    }

    [Fact]
    public async Task Read_ShouldNotInventCurrency_WhenThePageDoesNotStateIt()
    {
        string html = Page("", new
        {
            title = "Fashion runway", type = "EVENT", content = "<p>Example event.</p>",
            eventSchedules = new[] { Schedule("2026-11-21T17:00:00+05:00", exact: true, minimum: "200000") },
        });

        (await Read(html)).Captures.Single().Structured[0].GetProperty("offers").TryGetProperty("priceCurrency", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Read_ShouldFollowOnlyVisiblePublicDiscoveryLinks_AndNeverClaimACompleteCategory()
    {
        string html =
            """
            <html><body><main>
            <a href="/ru/fashion/2026/10/14/fashline-26?utm_source=mail">Runway</a>
            <a href="/ru/fashion/2026/10/14/fashline-26">Runway again</a>
            <a href="/ru/znaniya/2026/07/10/moda-yoli">Designer call</a>
            <a href="/ru/exhibitions">Exhibitions</a>
            <a href="/ru/znaniya">Unrelated section archive</a>
            <a href="/ru/fashion">Self</a>
            <a href="/api/materials?page=2">Internal API</a>
            <a href="/_next/data/build/list.json">Next payload</a>
            <a href="/ru/search?q=мода">Search</a>
            <a href="https://afisha.uz.attacker.example/ru/fashion/2026/10/14/steal">Other host</a>
            <button>Показать ещё</button>
            </main></body></html>
            """;

        CaptureReadResult result = await Read(html, "https://www.afisha.uz/ru/fashion");

        result.Complete.Should().BeFalse();
        result.Captures.Should().BeEmpty();
        result.Discovered.Should().HaveCount(3);
        result.Discovered.Should().Contain(item => item.Url == Article && !item.IsListing);
        result.Discovered.Should().Contain(item => item.Url == "https://www.afisha.uz/ru/exhibitions" && item.IsListing);
        result.Discovered.Should().NotContain(item => item.Url.Contains("page=2", StringComparison.Ordinal));
    }

    [Fact]
    public async Task Read_ShouldKeepStableCanonicalIdentityAndProjection_WhenOnlyViewCountersChange()
    {
        object first = new { title = "Designer show", type = "EVENT", content = "<p>Clothing collections.</p>", viewsCount = 12 };
        object second = new { title = "Designer show", type = "EVENT", content = "<p>Clothing collections.</p>", viewsCount = 400 };

        Capture one = (await Read(Page("", first))).Captures.Single();
        Capture two = (await Read(Page("", second))).Captures.Single();

        one.ExternalKey.Should().Be(two.ExternalKey);
        one.ContentProjection.Should().Be(two.ContentProjection);
    }

    [Fact]
    public async Task Read_ShouldIncludeMediaAndRegistrationLinksInTheContentProjection()
    {
        object material = new { title = "Designer show", type = "EVENT", content = "<p>Clothing collections.</p>" };
        Capture first = (await Read(Page("<img src='/uploads/one.jpg'>", material))).Captures.Single();
        Capture second = (await Read(Page("<img src='/uploads/two.jpg'>", material))).Captures.Single();

        first.Media.Should().ContainSingle();
        first.ContentProjection.Should().NotBe(second.ContentProjection);
    }

    [Fact]
    public async Task Read_ShouldPreserveFunctionalRegistrationQueryButDropTracking()
    {
        string html = Page("", new
        {
            title = "Открыт прием заявок на конкурс моды", type = "ARTICLE",
            content = "<p>Designer call.</p><a href='https://organizer.example/apply?event=7&amp;utm_source=afisha'>Apply</a>",
        });

        (await Read(html)).Captures.Single().Links.Single().Url.Should().Be("https://organizer.example/apply?event=7");
    }

    [Fact]
    public async Task Read_ShouldIgnoreACrossHostCanonical_AndUseTheCurrentArticle()
    {
        string html = Page("", new { title = "Designer fair", type = "EVENT", content = "<p>Garments.</p>" })
            .Replace(Article, "https://attacker.example/fair", StringComparison.Ordinal);

        (await Read(html)).Captures.Single().ExternalKey.Should().Be(Article);
    }

    [Fact]
    public async Task Read_ShouldFallBackToJsonLdAndVisibleContent_WhenFlightScriptIsMalformed()
    {
        string html =
            """
            <html lang="ru"><body><article><h1>Textile showcase</h1>
            <div class="content_content__fixture"><p>Textile and designer display.</p></div></article>
            <script>self.__next_f.push(not_json)</script>
            <script type="application/ld+json">{"@type":"Event","startDate":"2026-11-21"}</script>
            </body></html>
            """;

        Capture capture = (await Read(html)).Captures.Single();

        capture.Structured[0].GetProperty("name").GetString().Should().Be("Textile showcase");
        capture.Structured[0].GetProperty("description").GetString().Should().Be("Textile and designer display.");
        capture.Structured[0].GetProperty("startDate").GetString().Should().Be("2026-11-21");
    }

    [Theory]
    [InlineData("https://www.afisha.uz/ru/fashion", "application/xml", "<urlset />", false)]
    [InlineData("https://afisha.uz.attacker.example/ru/fashion", "text/html", "<html />", false)]
    [InlineData("https://www.afisha.uz/api/materials", "text/html", "<html />", false)]
    [InlineData("https://www.afisha.uz/ru/exhibitions", "text/html", "<html />", true)]
    public void CanRead_ShouldBindOnlyItsPublicHtmlRoutes(string url, string contentType, string body, bool expected)
    {
        new AfishaCaptureReader().CanRead(Document(body, url, contentType)).Should().Be(expected);
    }

    [Fact]
    public async Task Read_ShouldRespectCancellation()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        Func<Task> read = () => new AfishaCaptureReader().ReadAsync(Document("<html />", Article), cancelled.Token);

        await read.Should().ThrowAsync<OperationCanceledException>();
    }

    [Theory]
    [InlineData("40", true)]
    [InlineData("af", false)]
    public async Task Read_ShouldResolveUtf8FlightTextAcrossScriptChunks(string id, bool textRecordFirst)
    {
        const string body = "<p>Организаторы: Iteca Exhibitions и ICA Eurasia Group.</p>\n<p>Телефон: (+998) 71−205−1414.</p>";
        string materialPage = Page("<footer>publisher@example.uz</footer>", new
        {
            title = "Выставка", type = "EVENT", content = "$" + id, tags = new[] { "выставка", "iteca" },
        });
        string record = $"{id}:T{Encoding.UTF8.GetByteCount(body):x},{body}\n";
        int middle = record.Length / 2;
        string scripts = Flight(record[..middle]) + Flight(record[middle..]);
        string html = materialPage.Replace(textRecordFirst ? "<body>" : "</body>",
            textRecordFirst ? "<body>" + scripts : scripts + "</body>", StringComparison.Ordinal);

        Capture capture = (await Read(html)).Captures.Single();

        capture.Structured[0].GetProperty("description").GetString().Should().Contain("ICA Eurasia Group")
            .And.Contain("71−205−1414").And.NotContain("$" + id).And.NotContain("publisher@example.uz");
        JsonElement native = capture.Structured[1].GetProperty("sourceFields");
        native.GetProperty("fields").GetProperty("content").GetString().Should().Be("$" + id);
        native.GetProperty("contentHtml").GetString().Should().Be(body);
        capture.Structured[0].GetProperty("description").GetString().Should().Contain("Group.\nТелефон");
        capture.Structured[1].GetProperty("sourceTags").GetArrayLength().Should().Be(2);
    }

    [Fact]
    public async Task Read_ShouldResolveJsonStringRowsAndCaptureOnlyArticleContactLinks()
    {
        const string body = "<p>Registration.</p><a href='mailto:events@example.uz'>Email</a><a href='tel:+998712051414'>Phone</a>";
        string html = Page("", new { title = "Fair", type = "EVENT", content = "$1a" })
            .Replace("</body>", Flight("1a:" + JsonSerializer.Serialize(body) + "\n") + "<footer>footer@example.uz</footer></body>", StringComparison.Ordinal);

        Capture capture = (await Read(html)).Captures.Single();

        capture.Structured[1].GetProperty("contacts").EnumerateArray().Select(value => value.GetString())
            .Should().Equal("events@example.uz", "+998712051414");
    }

    [Theory]
    [InlineData("$40", "")]
    [InlineData("$40", "40:Tffff,<p>incomplete</p>\n")]
    public async Task Read_ShouldFallBackWithAWarningRatherThanStoreAnUnresolvedReference(string reference, string record)
    {
        string html = Page("<div data-content><p>Visible article body.</p></div>", new
        {
            title = "Fair", type = "EVENT", content = reference,
        }).Replace("</body>", Flight(record) + "</body>", StringComparison.Ordinal);

        Capture capture = (await Read(html)).Captures.Single();

        capture.Structured[0].GetProperty("description").GetString().Should().Be("Visible article body.");
        capture.Warnings.Should().Contain("source_content_reference_unresolved");
    }

    [Fact]
    public async Task Read_ShouldKeepCounterSnapshotsOutsideTheSemanticContentHash()
    {
        Capture first = (await Read(Page("", new { title = "Fair", type = "EVENT", content = "<p>Stable body.</p>", viewsCount = 10 }))).Captures.Single();
        Capture second = (await Read(Page("", new { title = "Fair", type = "EVENT", content = "<p>Stable body.</p>", viewsCount = 11 }))).Captures.Single();

        second.ContentProjection.Should().Be(first.ContentProjection);
        second.Structured[1].GetProperty("sourceFields").GetProperty("fields").GetProperty("viewsCount").GetInt32().Should().Be(11);
    }

    private static string Flight(string chunk)
    {
        return "<script>self.__next_f.push(" + JsonSerializer.Serialize(new object[] { 1, chunk }) + ")</script>";
    }

    private static async Task<CaptureReadResult> Read(string html, string url = Article)
    {
        Result<CaptureReadResult> result = await new AfishaCaptureReader().ReadAsync(Document(html, url));
        return result is Result<CaptureReadResult>.Success success ? success.Value
            : throw new Xunit.Sdk.XunitException(((Result<CaptureReadResult>.Failure)result).Error.Message);
    }

    private static FetchedDocument Document(string html, string url, string contentType = "text/html")
    {
        return new FetchedDocument
        {
            RequestUrl = new Uri(url), FinalUrl = new Uri(url), StatusCode = 200, ContentType = contentType,
            Body = Encoding.UTF8.GetBytes(html), FetchedAtUtc = new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero),
        };
    }

    private static string Page(string extra, object material)
    {
        var source = new Dictionary<string, object?> { ["@type"] = "Material" };
        foreach (JsonProperty property in JsonSerializer.SerializeToElement(material).EnumerateObject())
            source[property.Name] = property.Value.Clone();
        string row = "5:" + JsonSerializer.Serialize(new object[] { "$", "$L40", new { }, new { initialData = source } }) + "\n";
        string push = JsonSerializer.Serialize(new object[] { 1, row });
        return $"<html lang='ru'><head><link rel='canonical' href='{Article}'></head><body><article><h1>Visible fallback</h1>{extra}</article><script>self.__next_f.push({push})</script></body></html>";
    }

    private static object Schedule(string start, bool exact, string? end = null, string? minimum = null)
    {
        return new
        {
            startedAt = start, endedAt = end, isExactStartTime = exact, isExactEndTime = exact,
            dates = new[] { start[..10] }, place = new { name = "Fixture venue", address = "Example street", city = new { name = "Ташкент" } },
            remoteProperties = new { showgo = new { attendanceMinPrice = minimum } },
        };
    }
}
