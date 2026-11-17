using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Uzexpocentre;

namespace HarvestAdapters.Tests.Unit.Sources.Uzexpocentre;

/// <summary>Reduced official calendar tables protect printed years, carried cells and row identities.</summary>
public sealed class UzexpocentreCaptureReaderTests
{
    private const string Calendar = "https://www.uzexpocentre.uz/en/menu/krgazmalar-rezhasi";

    [Theory]
    [InlineData("17.11", "2025-11-17", "2025-11-17")]
    [InlineData("12-14.10", "2025-10-12", "2025-10-14")]
    [InlineData("29.09-01.10", "2025-09-29", "2025-10-01")]
    public async Task Read_ShouldBindDayAndMonthRangesToThePrintedCalendarYear(string rawDate, string start, string end)
    {
        Capture capture = (await Read(Page(2025, Row("7.", "Textile fair", rawDate)))).Captures.Single();
        JsonElement node = capture.Structured.Single();

        node.GetProperty("startDate").GetString().Should().Be(start);
        node.GetProperty("endDate").GetString().Should().Be(end);
        node.GetProperty("uzexpocentre").GetProperty("calendarYear").GetInt32().Should().Be(2025);
        capture.PublishedAtUtc.Should().BeNull();
    }

    [Fact]
    public async Task Read_ShouldNotInferTheCalendarYearFromAnEventTitleFooterOrFetchDate()
    {
        string html = Page(null, Row("7.", "Textile Fair 2026", "12-14.10")) + "<footer>Copyright 2026</footer>";

        Capture capture = (await Read(html)).Captures.Single();
        JsonElement node = capture.Structured.Single();

        node.TryGetProperty("startDate", out _).Should().BeFalse();
        node.GetProperty("uzexpocentre").GetProperty("calendarYear").ValueKind.Should().Be(JsonValueKind.Null);
        capture.Warnings.Should().Contain("calendar_date_unknown");
    }

    [Fact]
    public async Task Read_ShouldCarryOrganizerDateAndPavilionAcrossEachRowspan_WhileKeepingDifferentEventsDistinct()
    {
        string rows =
            """
            <tr><td>7.</td><td>Heimtextil</td>
            <td rowspan="3"><a href="https://organizer.example/participation">Fixture organizer</a></td>
            <td rowspan="3">12-14.10</td><td rowspan="3">Pavilions 1, 2</td></tr>
            <tr><td>8.</td><td>Texworld</td></tr>
            <tr><td>9.</td><td>Apparel Sourcing</td></tr>
            """;

        CaptureReadResult result = await Read(Page(2026, rows));

        result.Complete.Should().BeFalse();
        result.Captures.Should().HaveCount(3);
        result.Captures.Select(capture => capture.ExternalKey).Distinct(StringComparer.Ordinal).Should().HaveCount(3);
        result.Captures.Select(capture => capture.OriginKey).Distinct(StringComparer.Ordinal).Should().HaveCount(3);
        foreach (Capture capture in result.Captures)
        {
            capture.Url.Should().Be(Calendar);
            capture.OriginKey.Should().Be(capture.ExternalKey);
            capture.ExternalKey.Should().StartWith(Calendar + "#harvest-row-");
            capture.Links.Single().Url.Should().Be("https://organizer.example/participation");
            JsonElement node = capture.Structured.Single();
            node.GetProperty("startDate").GetString().Should().Be("2026-10-12");
            node.GetProperty("endDate").GetString().Should().Be("2026-10-14");
            JsonElement evidence = node.GetProperty("uzexpocentre");
            evidence.GetProperty("organizerRaw").GetString().Should().Be("Fixture organizer");
            evidence.GetProperty("dateRaw").GetString().Should().Be("12-14.10");
            evidence.GetProperty("pavilion").GetString().Should().Be("Pavilions 1, 2");
            evidence.GetProperty("participation").GetProperty("exhibitor").GetString().Should().Be("unknown");
        }
    }

    [Theory]
    [InlineData("<p>FIXTURE EXHIBITIONS</p><p>+00000 000-00-00</p><p>post@organizer.example</p><p>www.organizer.example</p>",
        "FIXTURE EXHIBITIONS", null)]
    [InlineData("<p>FIXTURE EXPO GROUP</p><p>+00000 000-00-00</p><p><a href=\"mailto:info@organizer.example\">info@organizer.example</a></p><p><a href=\"http://www.organizer.example\">www.organizer.example</a></p>",
        "FIXTURE EXPO GROUP", "http://www.organizer.example/")]
    [InlineData("<p>FIXTURE-LLP</p><p>+00000 000-00-00 &nbsp;<a href=\"mailto:info@organizer.example\">info@organizer.example</a></p>",
        "FIXTURE-LLP", null)]
    [InlineData("<p>&laquo;Fixture&raquo;</p><p>Association</p><p>00-000-00-00</p><p>00-000-00-00</p>", "«Fixture» Association", null)]
    [InlineData("<p>&ldquo;Fixture Enterprise Inc.&rdquo;</p><p>Tel +0 000 000 0000</p>", "“Fixture Enterprise Inc.”", null)]
    [InlineData("<p>Ministry of fixtures&nbsp;</p><p>Web-site: <a href=\"http://www.organizer.example\">www.organizer.example</a></p><p>+00000 000-00-00</p>",
        "Ministry of fixtures", "http://www.organizer.example/")]
    [InlineData("<p>Fixturemesse Fixturemesse@organizer.example +00000 000-00-00</p>", "Fixturemesse", null)]
    [InlineData("<p>&laquo;Fixture Fashion week&raquo; Co Ltd.</p>", "«Fixture Fashion week» Co Ltd.", null)]
    public async Task Read_ShouldKeepPrintedContactsOutOfTheOrganizerName(string cell, string name, string? url)
    {
        // Trimmed from the live calendar's organizer cells (2026-11-17), with synthetic contacts: the name came out as
        // "NAME +phone email site" and reached the event wire as its organizer.
        string row = $"<tr><td>2.</td><td>Textile fair</td><td>{cell}</td><td>08-10.09</td><td>№№1,2</td></tr>";

        JsonElement node = (await Read(Page(2026, row))).Captures.Single().Structured.Single();

        node.GetProperty("organizer").GetProperty("name").GetString().Should().Be(name);
        if (url is null)
            node.GetProperty("organizer").TryGetProperty("url", out _).Should().BeFalse();
        else
            node.GetProperty("organizer").GetProperty("url").GetString().Should().Be(url);
        node.GetProperty("uzexpocentre").GetProperty("organizerRaw").GetString().Should().StartWith(name.Split(' ')[0]);
        node.GetProperty("startDate").GetString().Should().Be("2026-09-08");
    }

    [Fact]
    public async Task Read_ShouldOmitTheOrganizer_WhenTheCellIsEmpty()
    {
        string row = "<tr><td>2.</td><td>Textile fair</td><td>\n\n</td><td>08-10.09</td><td>№№1,2</td></tr>";

        JsonElement node = (await Read(Page(2026, row))).Captures.Single().Structured.Single();

        node.TryGetProperty("organizer", out _).Should().BeFalse();
    }

    [Fact]
    public async Task Read_ShouldKeepTheNormalRowKeyStable_WhenItsDatesAndPrintedRowNumberChange()
    {
        Capture first = (await Read(Page(2026, Row("7.", "Textile fair", "12-14.10")))).Captures.Single();
        Capture second = (await Read(Page(2026, Row("9.", "Textile fair", "19-21.10")))).Captures.Single();

        first.ExternalKey.Should().Be(second.ExternalKey);
        first.OriginKey.Should().Be(second.OriginKey);
        first.ContentProjection.Should().NotBe(second.ContentProjection);
        first.Url.Should().Be(second.Url);
    }

    [Fact]
    public async Task Read_ShouldDisambiguateRepeatedTitles_WithoutAddingThePrintedNumberToNormalRowKeys()
    {
        Capture single = (await Read(Page(2026, Row("1.", "Designer fair", "01-03.11")))).Captures.Single();
        CaptureReadResult repeated = await Read(Page(2026,
            Row("7.", "Designer fair", "01-03.11") + Row("8.", "Designer fair", "15-17.11")));

        repeated.Captures.Should().HaveCount(2);
        repeated.Captures[0].ExternalKey.Should().Be(single.ExternalKey);
        repeated.Captures[1].ExternalKey.Should().NotBe(single.ExternalKey);
        repeated.Captures.Select(capture => capture.Url).Distinct(StringComparer.Ordinal).Should().ContainSingle().Which.Should().Be(Calendar);
    }

    [Fact]
    public async Task Read_ShouldNotCarryAPreviousTablesYearIntoAnUnheadedTable()
    {
        string html = Page(2025, Row("1.", "Earlier textile event", "01-03.11"))
            .Replace("</div>", Table(Row("1.", "Undated textile event", "05-07.11")) + "</div>", StringComparison.Ordinal);

        CaptureReadResult result = await Read(html);

        result.Captures.Should().HaveCount(2);
        result.Captures[0].Structured.Single().GetProperty("startDate").GetString().Should().Be("2025-11-01");
        result.Captures[1].Structured.Single().TryGetProperty("startDate", out _).Should().BeFalse();
    }

    [Theory]
    [InlineData("31.02")]
    [InlineData("18-17.11")]
    public async Task Read_ShouldKeepInvalidOrReversedRangesUnknown(string rawDate)
    {
        Capture capture = (await Read(Page(2026, Row("1.", "Textile fair", rawDate)))).Captures.Single();

        capture.Structured.Single().TryGetProperty("startDate", out _).Should().BeFalse();
        capture.Warnings.Should().Contain("calendar_date_unknown");
    }

    private static async Task<CaptureReadResult> Read(string html)
    {
        Result<CaptureReadResult> result = await new UzexpocentreCaptureReader().ReadAsync(new FetchedDocument
        {
            RequestUrl = new Uri(Calendar), FinalUrl = new Uri(Calendar), StatusCode = 200, ContentType = "text/html",
            Body = Encoding.UTF8.GetBytes(html), FetchedAtUtc = new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero),
        });
        return result is Result<CaptureReadResult>.Success success ? success.Value
            : throw new Xunit.Sdk.XunitException(((Result<CaptureReadResult>.Failure)result).Error.Message);
    }

    private static string Page(int? year, string rows) => "<html><body><div class='content'>"
        + (year is null ? "<p><strong>EXHIBITION PLAN</strong></p>" : $"<p><strong>EXHIBITION PLAN</strong></p><p><strong>JULY–DECEMBER {year}</strong></p><p>&nbsp;</p>")
        + Table(rows) + "</div></body></html>";

    private static string Table(string rows) => "<table><tbody><tr><td>№</td><td>Topic</td><td>Organizer</td><td>Date</td><td>Venue (Pavilion №)</td></tr>"
        + rows + "</tbody></table>";

    private static string Row(string number, string title, string dates) => $"<tr><td>{number}</td><td>{title}</td><td>Fixture organizer</td><td>{dates}</td><td>Pavilion 1</td></tr>";
}
