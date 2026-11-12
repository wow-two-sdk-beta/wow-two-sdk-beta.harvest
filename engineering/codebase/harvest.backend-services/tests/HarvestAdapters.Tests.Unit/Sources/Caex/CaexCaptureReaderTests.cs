using System.Text;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Caex;

namespace HarvestAdapters.Tests.Unit.Sources.Caex;

public sealed class CaexCaptureReaderTests
{
    private static readonly Uri Calendar = new("https://caexuzbekistan.com/events-calendar/");

    [Theory]
    [InlineData("November 25–27, 2026", "2026-11-25", "2026-11-27")]
    [InlineData("5 December, 2026", "2026-12-05", null)]
    [InlineData("January 25-27, 2027", "2027-01-25", "2027-01-27")]
    [InlineData("November 25–27", null, null)]
    [InlineData("November 27–25, 2026", null, null)]
    public async Task Reads_only_dates_stated_in_the_calendar_row(string date, string? starts, string? ends)
    {
        var read = await new CaexCaptureReader().ReadAsync(Document(Row(date)));
        var result = read.Should().BeOfType<Result<CaptureReadResult>.Success>().Subject.Value;
        var capture = result.Captures.Should().ContainSingle().Subject;
        result.Complete.Should().BeFalse();
        capture.ExternalKey.Should().Be("https://caexuzbekistan.com/2026/09/02/titf-2026/");
        capture.PublishedAtUtc.Should().BeNull();
        var item = capture.Structured.Single();
        (item.TryGetProperty("startDate", out var start) ? start.GetString() : null).Should().Be(starts);
        (item.TryGetProperty("endDate", out var end) ? end.GetString() : null).Should().Be(ends);
        item.GetProperty("caex").GetProperty("dateRaw").GetString().Should().Be(date);
        item.TryGetProperty("offers", out _).Should().BeFalse();
        capture.Links.Should().Contain(link => link.Url == "https://titf.uz/");
    }

    [Fact]
    public async Task Reordered_rows_and_reschedules_keep_the_event_identity()
    {
        var reader = new CaexCaptureReader();
        var old = (await reader.ReadAsync(Document(Row("November 25–27, 2026"))))
            .Should().BeOfType<Result<CaptureReadResult>.Success>().Subject.Value.Captures.Single();
        var updated = (await reader.ReadAsync(Document(Row("November 26–28, 2026") + Row("November 26–28, 2026"))))
            .Should().BeOfType<Result<CaptureReadResult>.Success>().Subject.Value.Captures.Single();
        updated.ExternalKey.Should().Be(old.ExternalKey);
        updated.ContentProjection.Should().NotBe(old.ContentProjection);
    }

    [Fact]
    public async Task Ignores_unrelated_or_offsite_links_and_fails_on_unrecognized_pages()
    {
        var reader = new CaexCaptureReader();
        reader.Allows(Calendar).Should().BeTrue();
        reader.Allows(new("https://titf.uz/")).Should().BeFalse();
        reader.Allows(new("https://caexuzbekistan.com/wp-json/")).Should().BeFalse();
        reader.Allows(new("https://caexuzbekistan.com:444/events-calendar/")).Should().BeFalse();
        string html = Row("November 25–27, 2026").Replace("href='/2026/09/02/titf-2026/'", "href='https://titf.uz/2026/09/02/other/'");
        (await reader.ReadAsync(Document(html))).Should().BeOfType<Result<CaptureReadResult>.Failure>();
        (await reader.ReadAsync(Document("<html><title>Verify your browser</title></html>")))
            .Should().BeOfType<Result<CaptureReadResult>.Failure>();
    }

    private static FetchedDocument Document(string html) => new()
    {
        RequestUrl = Calendar,
        FinalUrl = Calendar,
        Body = Encoding.UTF8.GetBytes(html),
        FetchedAtUtc = new DateTimeOffset(2026, 11, 9, 8, 0, 0, TimeSpan.Zero),
        StatusCode = 200,
        ContentType = "text/html",
    };

    private static string Row(string date) => $"""
        <article class="caex-event-row">
          <h3 class="wp-block-post-title"><a href='/2026/09/02/titf-2026/'>TITF 2026</a></h3>
          <p class="caex-event-row-date">{date}</p>
          <p class="caex-event-row-organizer">Iteca Exhibitions</p>
          <p class="wp-block-post-excerpt__excerpt">The Silk Road tourism exhibition.</p>
          <div class="caex-event-row-website"><a href='https://titf.uz/'>Website</a></div>
        </article>
        """;
}
