using System.Text;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Hata;

namespace HarvestAdapters.Tests.Unit.Sources.Hata;

public sealed class HataCaptureReaderTests
{
    private const string Listing = "https://hata.uz/listings/object/apartment-123";

    [Fact]
    public async Task DiscoversDistinctCardsAndTheNextPageOfTheSameSearch()
    {
        var reader = new HataCaptureReader();
        var result = await reader.ReadAsync(Page(HataSourceAdapter.RentCatalog, """
            <article>
              <a href="/listings/object/apartment-123">Apartment</a>
              <a href="/listings/object/renamed-flat-123">Same apartment</a>
              <a href="https://example.org/listings/object/other-456">Different site</a>
            </article>
            <a href="/listings/object/outside-the-catalog-188">Related listing</a>
            <a href="/listings/sale/flats/tashkent?page=2">Sale page</a>
            <a href="/listings/rent/flats/tashkent?page=3">Skip a page</a>
            <a href="/listings/rent/flats/tashkent?page=2">Next</a>
            """));

        var read = result.Should().BeOfType<Result<CaptureReadResult>.Success>().Subject.Value;
        read.Discovered.Should().Equal(
            new DiscoveredUrl { Url = Listing, ExternalKey = "123" },
            new DiscoveredUrl { Url = HataSourceAdapter.RentCatalog + "?page=2", IsListing = true });
        read.Complete.Should().BeFalse();
    }

    [Fact]
    public async Task SavesFullPublishedDescriptionAndAttributesForTheRequestedRental()
    {
        var reader = new HataCaptureReader();
        var result = await reader.ReadAsync(Page(Listing, RentalDetail));

        var capture = result.Should().BeOfType<Result<CaptureReadResult>.Success>().Subject.Value.Captures
            .Should().ContainSingle().Subject;
        capture.ExternalKey.Should().Be("123");
        capture.Url.Should().Be(Listing);
        capture.PublishedAtUtc.Should().BeNull();
        capture.Media.Should().ContainSingle();
        var product = capture.Structured[0];
        product.GetProperty("description").GetString().Should().Be("Full rental description with the deposit and lease terms.");
        product.GetProperty("offers").GetProperty("price").GetString().Should().Be("600");
        var fields = capture.Structured[1].GetProperty("sourceFields").GetProperty("fields");
        fields.GetProperty("attributes").GetProperty("Количество комнат").GetString().Should().Be("2");
        fields.GetProperty("attributes").GetProperty("Ремонт").GetString().Should().Be("Хороший");
        fields.GetProperty("areaM2").GetString().Should().Be("54");
    }

    [Theory]
    [InlineData("/listings/sale/secondary_flats", "https://hata.uz/listings/object/apartment-123")]
    [InlineData("/listings/rent/secondary_flats", "https://hata.uz/listings/object/apartment-999")]
    public async Task RejectsDetailsOutsideTheRequestedRentalScope(string category, string declaredUrl)
    {
        var reader = new HataCaptureReader();
        string detail = RentalDetail.Replace("/listings/rent/secondary_flats", category, StringComparison.Ordinal)
            .Replace(Listing, declaredUrl, StringComparison.Ordinal);
        var result = await reader.ReadAsync(Page(Listing, detail));

        result.Should().BeOfType<Result<CaptureReadResult>.Failure>();
        reader.Allows(new Uri("https://hata.uz/api/listings")).Should().BeFalse();
    }

    private static FetchedDocument Page(string url, string html)
    {
        var address = new Uri(url);
        return new()
        {
            RequestUrl = address, FinalUrl = address, Body = Encoding.UTF8.GetBytes(html),
            StatusCode = 200, ContentType = "text/html", FetchedAtUtc = DateTimeOffset.UnixEpoch,
        };
    }

    private const string RentalDetail = """
        <script type="application/ld+json">
        {"@type":"BreadcrumbList","itemListElement":[{"item":"https://hata.uz/listings/rent/secondary_flats"}]}
        </script>
        <script type="application/ld+json">
        {
          "@type":"RealEstateListing","url":"https://hata.uz/listings/object/apartment-123","name":"Two rooms","description":"Short summary",
          "about":{
            "@type":"Apartment","address":{"addressLocality":"Ташкент","addressRegion":"Чиланзарский район"},
            "numberOfRooms":2,"floorLevel":3,"floorSize":{"value":54},"image":["https://hata.uz/photo.jpg"]
          },
          "offers":{"price":"600","priceCurrency":"USD"}
        }
        </script>
        <main class="property-detail">
          <section><h2>Описание</h2><div>Full rental description with the deposit and lease terms.</div></section>
          <section><h2>Характеристики</h2><div><div><span>Ремонт</span><span>Хороший</span></div></div></section>
        </main>
        """;
}
