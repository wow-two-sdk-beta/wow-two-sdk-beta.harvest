using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.BirBir;

namespace HarvestAdapters.Tests.Unit.Sources.BirBir;

public sealed class BirBirCaptureReaderTests
{
    private const string Apartment = "https://birbir.uz/uz/toshkent/cat/kochmas-mulk/ijara/kvartiralar/o/fixture-1842";
    private readonly BirBirCaptureReader reader = new();

    [Fact]
    public async Task Read_ShouldInterpretUzbekStructuredFieldsAndKeepUnconfirmedValuesEmpty()
    {
        var result = await reader.ReadAsync(Document("1842"));
        var capture = result.Should().BeOfType<Result<CaptureReadResult>.Success>().Subject.Value.Captures.Single();
        var fields = capture.Structured[1].GetProperty("sourceFields").GetProperty("fields");

        fields.GetProperty("locale").GetString().Should().Be("uz");
        fields.GetProperty("priceText").GetString().Should().Be("3250000.25 UZS");
        fields.GetProperty("priceAmount").GetDecimal().Should().Be(3250000.25m);
        fields.GetProperty("areaSquareMetres").GetDecimal().Should().Be(45.5m);
        fields.GetProperty("roomCount").GetInt32().Should().Be(2);
        fields.GetProperty("floor").GetInt32().Should().Be(4);
        fields.GetProperty("buildingFloors").GetInt32().Should().Be(9);
        fields.GetProperty("district").ValueKind.Should().Be(JsonValueKind.Null);
        fields.GetProperty("locationText").GetString().Should().Be("Metro bekati");
        fields.GetProperty("offerValidFrom").GetString().Should().Be("2026-11-13");
        fields.GetProperty("publishedAtUtc").ValueKind.Should().Be(JsonValueKind.Null);
    }

    [Theory]
    [InlineData("1843")]
    [InlineData("")]
    public async Task Read_ShouldRejectAnUnrelatedProduct(string sku)
    {
        var result = await reader.ReadAsync(Document(sku));
        result.Should().BeOfType<Result<CaptureReadResult>.Failure>();
    }

    [Fact]
    public async Task Read_ShouldKeepEveryPublishedGalleryPhoto()
    {
        var photos = Enumerable.Range(1, 32).Select(index => $"https://img.birbir.uz/gallery/photo-{index}.jpg").ToArray();
        var result = await reader.ReadAsync(Document("1842", [.. photos, photos[0], "https://other.test/photo.jpg"]));
        var capture = result.Should().BeOfType<Result<CaptureReadResult>.Success>().Subject.Value.Captures.Single();

        capture.Media.Select(photo => photo.Url).Should().Equal(photos);
    }

    private static FetchedDocument Document(string sku, IReadOnlyList<string>? images = null) => new()
    {
        RequestUrl = new(Apartment), FinalUrl = new(Apartment), StatusCode = 200, ContentType = "text/html; charset=utf-8",
        FetchedAtUtc = new DateTimeOffset(2026, 11, 14, 9, 0, 0, TimeSpan.Zero),
        Body = Encoding.UTF8.GetBytes("""
            <html lang="uz"><head><script type="application/ld+json">
            {"@type":"Product","sku":"__SKU__","name":"Sinov kvartirasi","description":"Sinov uchun e’lon.","image":__IMAGES__,
             "offers":{"url":"__URL__","price":3250000.25,"priceCurrency":"UZS","validFrom":"2026-11-13"},
             "additionalProperty":[{"name":"Kvadrat","value":"45,5"},{"name":"xonalar","value":"2 xonali"},
                {"name":"Qavat","value":"4"},{"name":"Qavatlar soni","value":"9"}]}
            </script></head><body><main>
            <div data-testid="offer_delivery_address"><div><p>Toshkent, metro bekati</p><p>Metro bekati</p></div></div>
            </main></body></html>
            """.Replace("__SKU__", sku, StringComparison.Ordinal).Replace("__URL__", Apartment, StringComparison.Ordinal)
                .Replace("__IMAGES__", JsonSerializer.Serialize(images ?? []), StringComparison.Ordinal)),
    };
}
