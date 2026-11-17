using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Craftfair;

namespace HarvestAdapters.Tests.Unit.Extraction;

/// <summary>A reduced Craftfair event page through its reader, the rules extractor and the event mapping: the call's
/// deadline reaches <c>wow2.event.v1</c> as a typed application deadline with the status the page prints.</summary>
public sealed class CraftfairDeadlineTests
{
    private const string Article = "https://craftfair.uz/events/craft-fair-2026?lang=en";

    [Fact]
    public async Task Extract_ShouldReadAClosedCallsDeadline_WithStatusClosed()
    {
        var post = await ExtractAsync(Page("Applications are closed"));

        var deadline = post.Dates.Should().ContainSingle(date => date.Role == DateRole.Deadline).Subject;
        deadline.Value.Should().Be("2026-10-26T23:59:59+05:00");
        deadline.Precision.Should().Be(DatePrecision.Second);
        deadline.Raw.Should().Be("26 October, 2026");
        deadline.Status.Should().Be(CallStatus.Closed);
        var mapped = post.Extras["event"].GetProperty("deadlines").EnumerateArray().Single();
        mapped.GetProperty("kind").GetString().Should().Be("application");
        mapped.GetProperty("value").GetString().Should().Be("2026-10-26T23:59:59+05:00");
        mapped.GetProperty("status").GetString().Should().Be("closed");
        post.Dates.Should().Contain(date => date.Role == DateRole.Start && date.Value == "2026-11-25", "the event span stays apart from the call");
    }

    [Fact]
    public async Task Extract_ShouldReadAnOpenCall_WhenTheBadgeSaysSo()
    {
        var post = await ExtractAsync(Page("Applications are open"));

        post.Extras["event"].GetProperty("deadlines")[0].GetProperty("status").GetString().Should().Be("open");
    }

    private static async Task<PostEnvelope> ExtractAsync(string html)
    {
        var document = new FetchedDocument
        {
            RequestUrl = new Uri(Article), FinalUrl = new Uri(Article), StatusCode = 200, ContentType = "text/html",
            Body = Encoding.UTF8.GetBytes(html), FetchedAtUtc = new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero),
        };
        var capture = Value(await new CraftfairCaptureReader().ReadAsync(document)).Captures.Single();
        return Value(await new RulesPostExtractor().ExtractAsync(capture, new ExtractionRequest { Family = SourceFamily.Site }));
    }

    private static T Value<T>(Result<T> result) where T : notnull => result is Result<T>.Success success ? success.Value
        : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);

    private static string Page(string status) => $"""
        <html lang="en"><body><h1 class="h1">Craft Fair 2026</h1>
        <article class="event-detail-panel"><div class="detail-rich-text"><p>Garments and artisan crafts.</p></div></article>
        <aside class="event-side-panel"><span class="status-badge">{status}</span>
        <div class="countdown-minimal" data-deadline="2026-10-26T23:59:59+05:00"></div>
        <div class="sidebar-meta-item"><span class="meta-label">Event dates</span><span class="meta-val">25 - 27 November, 2026</span></div>
        <div class="sidebar-meta-item"><span class="meta-label">Registration deadline</span><span class="meta-val highlight">26 October, 2026</span></div>
        <div class="sidebar-meta-item"><span class="meta-label">Location</span><span class="meta-val">Example expo centre</span></div>
        </aside></body></html>
        """;
}
