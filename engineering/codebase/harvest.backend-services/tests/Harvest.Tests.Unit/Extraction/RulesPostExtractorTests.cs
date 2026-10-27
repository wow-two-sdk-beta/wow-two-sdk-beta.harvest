using System.Text.Json;
using AwesomeAssertions;
using Harvest.Tests.Unit.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace Harvest.Tests.Unit.Extraction;

/// <summary>Checks deterministic extraction and schema mapping from offline fixtures.</summary>
public sealed class RulesPostExtractorTests
{
    [Fact]
    public async Task Extract_ShouldMapEventFieldsWithoutAnAiProvider()
    {
        var read = await new JsonLdEventReader().ReadAsync(CaptureReaderTests.Document("""
            <html lang="en"><script type="application/ld+json">{"@type":"Event","@id":"https://mstar.uz/event/17",
              "name":"The concert","description":"An event with tickets.","startDate":"2026-11-03T19:00:00+05:00",
              "endDate":"2026-11-03T22:00:00+05:00","organizer":{"name":"Mstar"},
              "location":{"name":"Hall","address":{"addressLocality":"Tashkent","streetAddress":"Main road"}},
              "offers":{"price":"150000","priceCurrency":"UZS","url":"https://mstar.uz/tickets/17"},
              "image":"/poster.jpg"}</script></html>
            """, "https://mstar.uz/event/17"));
        var result = await new RulesPostExtractor().ExtractAsync(read.Data().Captures[0], new() { Family = SourceFamily.Structured });
        result.IsSuccess.Should().BeTrue();
        var post = result.Data();
        post.Schema.Should().Be("wow2.post.v1");
        post.Kind.Should().Be(PostKind.Event);
        post.Title.Should().Be("The concert");
        post.Dates.Should().Contain(date => date.Role == DateRole.Start && date.Value == "2026-11-03T19:00:00+05:00");
        post.Prices[0].Amount.Should().Be(150000);
        post.Location!.Mentions.Should().Contain(place => place.Kind == LocationKind.City && place.Text == "Tashkent");
        post.Media[0].Url.Should().Be("https://mstar.uz/poster.jpg");
        post.Extras["event"].GetProperty("schema").GetString().Should().Be("wow2.event.v1");
        post.Extras["event"].GetProperty("starts").GetProperty("precision").GetString().Should().Be("second");
        post.FieldSources["prices"].Tier.Should().Be(ExtractionTier.Deterministic);
        post.Extraction.Ai.Should().BeNull();
    }

    [Fact]
    public async Task Extract_ShouldPreserveAllDayAndMonthPrecision()
    {
        var post = await ExtractEvent("""{"@type":"Event","name":"Exhibition","startDate":"2026-11","endDate":"2026-12-03"}""");
        post.Dates.Single(date => date.Role == DateRole.Start).Precision.Should().Be(DatePrecision.Month);
        post.Dates.Single(date => date.Role == DateRole.Start).Value.Should().Be("2026-11");
        post.Dates.Single(date => date.Role == DateRole.End).Precision.Should().Be(DatePrecision.Day);
    }

    [Theory]
    [InlineData("2026-02-30")]
    [InlineData("2026-13")]
    [InlineData("tomorrow")]
    public async Task Extract_ShouldLeaveInvalidOrRelativeDatesUnresolved(string date)
    {
        var post = await ExtractEvent(JsonSerializer.Serialize(new Dictionary<string, object> { ["@type"] = "Event", ["name"] = "Show", ["startDate"] = date }));
        post.Dates.Should().BeEmpty();
        post.Extras["event"].GetProperty("starts").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Fact]
    public async Task Extract_ShouldKeepUnknownPriceUnknownAndExplicitFreeSeparate()
    {
        var unknown = await ExtractEvent("""{"@type":"Event","name":"Show","offers":{"priceCurrency":"UZS"}}""");
        unknown.Prices.Should().BeEmpty();
        var free = await ExtractEvent("""{"@type":"Event","name":"Show","offers":{"price":"0","priceCurrency":"UZS"}}""");
        free.Prices[0].Amount.Should().Be(0);
        free.Prices[0].Free.Should().BeTrue();
    }

    [Fact]
    public async Task Extract_ShouldNormalizePhonesAsPlainE164AndNeverReadAi()
    {
        var read = await new PageCaptureReader().ReadAsync(CaptureReaderTests.Document("""
            <html lang="uz"><main><h1>Toshkent tadbiri</h1><p>Narxi 150 000 so'm. Sana 03.11.2026.</p>
            <p>Telefon +998 (90) 123-45-67, 91 765 43 21. Email INFO@example.uz.</p></main></html>
            """, "https://voyage.uz/event/1"));
        var result = await new RulesPostExtractor().ExtractAsync(read.Data().Captures[0], new()
        {
            Family = SourceFamily.Page, Ai = AiMode.Full, AiAllowed = false, BudgetAllowsAi = false,
        });
        var post = result.Data();
        post.Contacts.Should().Contain(contact => contact.Kind == ContactKind.Phone && contact.Value == "+998901234567");
        post.Contacts.Should().Contain(contact => contact.Kind == ContactKind.Phone && contact.Value == "+998917654321");
        post.Contacts.Should().Contain(contact => contact.Kind == ContactKind.Email && contact.Value == "info@example.uz");
        post.Prices[0].Amount.Should().Be(150000);
        post.Prices[0].Currency.Should().Be("UZS");
        post.Dates.Should().Contain(date => date.Value == "2026-11-03");
        post.Language!.Code.Should().Be("uz");
        post.Extraction.TiersRun.Should().Equal(ExtractionTier.Deterministic, ExtractionTier.Rules);
        post.Extraction.Ai.Should().BeNull();
        post.Flags.Should().Contain("ai_skipped:capability_unavailable");
    }

    [Fact]
    public async Task Extract_ShouldReadHiddenTelLinkAndKeepUncertainLanguageUnknown()
    {
        var read = await new PageCaptureReader().ReadAsync(CaptureReaderTests.Document("<main><h1>X123</h1><a href='tel:+442071234567'>Call</a></main>"));
        var result = await new RulesPostExtractor().ExtractAsync(read.Data().Captures[0], new() { Family = SourceFamily.Page });
        result.Data().Contacts.Should().Contain(contact => contact.Value == "+442071234567");
        result.Data().Language!.Code.Should().Be("und");
        EventSchemaMapper.Map(result.Data()).Should().BeNull();
    }

    [Theory]
    [InlineData("The show with music", "en", "Latn")]
    [InlineData("Концерт в Ташкенте", "ru", "Cyrl")]
    [InlineData("Кўргазма учун чипта", "uz", "Cyrl")]
    public async Task Extract_ShouldIdentifyLanguageByRules(string text, string language, string script)
    {
        var capture = CaptureReaderTests.Document("<main>" + text + "</main>");
        var read = await new PageCaptureReader().ReadAsync(capture);
        var result = await new RulesPostExtractor().ExtractAsync(read.Data().Captures[0], new() { Family = SourceFamily.Page });
        result.Data().Language!.Code.Should().Be(language);
        result.Data().Language!.Script.Should().Be(script);
    }

    private static async Task<PostEnvelope> ExtractEvent(string json)
    {
        var read = await new JsonLdEventReader().ReadAsync(CaptureReaderTests.Document(json, "https://mstar.uz/event/17", "application/ld+json"));
        return (await new RulesPostExtractor().ExtractAsync(read.Data().Captures[0], new() { Family = SourceFamily.Structured })).Data();
    }
}
