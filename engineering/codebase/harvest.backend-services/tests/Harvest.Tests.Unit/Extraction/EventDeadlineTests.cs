using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Harvest.Tests.Unit.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace Harvest.Tests.Unit.Extraction;

/// <summary>Application deadlines a named reader states in its <c>schemaOrg.{reader}</c> namespace become typed
/// deadline dates and the <c>deadlines</c> list of <c>wow2.event.v1</c>, one case per reader shape.</summary>
public sealed class EventDeadlineTests
{
    [Fact]
    public async Task Extract_ShouldTypeACraftfairDeadline_WithItsPrecisionRawTextAndClosedStatus()
    {
        var post = await ExtractAsync("craftfair", new
        {
            reader = "craftfair", recordKind = "event", applicationDeadline = "2026-10-26T23:59:59+05:00",
            applicationDeadlineRaw = "26 October, 2026", applicationStatusRaw = "Applications are closed",
        });

        var deadline = post.Dates.Should().ContainSingle(date => date.Role == DateRole.Deadline).Subject;
        deadline.Value.Should().Be("2026-10-26T23:59:59+05:00");
        deadline.Precision.Should().Be(DatePrecision.Second);
        deadline.Raw.Should().Be("26 October, 2026");
        deadline.Status.Should().Be(CallStatus.Closed);
        Fields(Deadline(post)).Should().Equal("application", "2026-10-26T23:59:59+05:00", "second", "26 October, 2026", "closed");
        post.Extras["schemaOrg"].GetProperty("craftfair").GetProperty("applicationDeadline").GetString()
            .Should().Be("2026-10-26T23:59:59+05:00", "the reader namespace stays as recorded");
    }

    [Theory]
    [InlineData("Applications are open", "open")]
    [InlineData("Coming soon", "unknown")]
    public async Task Extract_ShouldReadACraftfairCallStatus_OnlyFromWhatTheBadgeSays(string badge, string status)
    {
        var post = await ExtractAsync("craftfair", new
        {
            reader = "craftfair", applicationDeadline = "2026-10-26", applicationDeadlineRaw = (string?)null, applicationStatusRaw = badge,
        });

        Deadline(post).GetProperty("status").GetString().Should().Be(status);
        Deadline(post).GetProperty("precision").GetString().Should().Be("day");
        Deadline(post).GetProperty("raw").GetString().Should().Be("2026-10-26", "with no printed text the stated value is the evidence");
    }

    [Fact]
    public async Task Extract_ShouldTypeAnUzexpocentreDeadline_WhenTheCalendarStatesOne()
    {
        var post = await ExtractAsync("uzexpocentre", new { reader = "uzexpocentre", recordKind = "event", applicationDeadline = "2026-09-01" });

        Fields(Deadline(post)).Should().Equal("application", "2026-09-01", "day", "2026-09-01", "unknown");
    }

    [Fact]
    public async Task Extract_ShouldTypeAnAfishaDeadline_FromItsEvidenceDate()
    {
        var post = await ExtractAsync("afisha", new
        {
            reader = "afisha", recordKind = "call",
            applicationDeadlineEvidence = new { raw = "Приём заявок до 15.11.2026 включительно.", date = "2026-11-15" },
        });

        Fields(Deadline(post)).Should().Equal("application", "2026-11-15", "day", "Приём заявок до 15.11.2026 включительно.", "unknown");
    }

    [Fact]
    public async Task Extract_ShouldListNoDeadline_WhenTheReaderStatesNone()
    {
        var afisha = await ExtractAsync("afisha", new { reader = "afisha", applicationDeadlineEvidence = new { raw = "Заявки принимаются.", date = (string?)null } });
        var uzexpocentre = await ExtractAsync("uzexpocentre", new { reader = "uzexpocentre", applicationDeadline = (string?)null });
        var craftfair = await ExtractAsync("craftfair", new { reader = "craftfair", applicationDeadline = "26 October", applicationStatusRaw = "Applications are closed" });

        foreach (var post in new[] { afisha, uzexpocentre, craftfair })
        {
            post.Dates.Should().NotContain(date => date.Role == DateRole.Deadline);
            post.Extras["event"].GetProperty("deadlines").GetArrayLength().Should().Be(0);
        }
    }

    [Fact]
    public async Task Map_ShouldReadTheReaderNamespace_WhenAStoredEnvelopePredatesTypedDeadlines()
    {
        var post = await ExtractAsync("craftfair", new
        {
            reader = "craftfair", applicationDeadline = "2026-10-26T23:59:59+05:00", applicationStatusRaw = "Applications are closed",
        });
        var stored = post with { Dates = post.Dates.Where(date => date.Role != DateRole.Deadline).ToList() };

        var mapped = EventSchemaMapper.Map(stored)!.Value.GetProperty("deadlines");

        mapped.GetArrayLength().Should().Be(1);
        mapped[0].GetProperty("value").GetString().Should().Be("2026-10-26T23:59:59+05:00");
        mapped[0].GetProperty("status").GetString().Should().Be("closed");
    }

    private static async Task<PostEnvelope> ExtractAsync(string reader, object evidence)
    {
        var node = JsonSerializer.SerializeToElement(new Dictionary<string, object?>
        {
            ["@context"] = "https://schema.org", ["@type"] = "Event", ["name"] = "Craft fair", ["description"] = "Artisan crafts.",
            ["startDate"] = "2026-11-25", [reader] = evidence,
        });
        var capture = new Capture
        {
            ExternalKey = "https://source.example/" + reader, Url = "https://source.example/" + reader,
            RequestUrl = "https://source.example/" + reader, FinalUrl = "https://source.example/" + reader,
            CapturedAtUtc = new DateTimeOffset(2026, 10, 27, 10, 0, 0, TimeSpan.Zero), Body = Encoding.UTF8.GetBytes(reader),
            ContentProjection = node.GetRawText(), Structured = [node],
        };
        return (await new RulesPostExtractor().ExtractAsync(capture, new() { Family = SourceFamily.Site })).Data();
    }

    private static JsonElement Deadline(PostEnvelope post) => post.Extras["event"].GetProperty("deadlines").EnumerateArray().Single();

    /// <summary>Reads one deadline's fields in wire order: kind, value, precision, raw, status.</summary>
    private static string?[] Fields(JsonElement deadline)
    {
        deadline.EnumerateObject().Select(property => property.Name).Should().Equal("kind", "value", "precision", "raw", "status");
        return deadline.EnumerateObject().Select(property => property.Value.GetString()).ToArray();
    }
}
