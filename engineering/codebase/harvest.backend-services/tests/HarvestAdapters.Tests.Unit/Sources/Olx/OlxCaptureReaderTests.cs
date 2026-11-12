using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Olx;

namespace HarvestAdapters.Tests.Unit.Sources.Olx;

/// <summary>Pins the ported OLX behaviors and guards identity, saved filters and inconclusive availability.</summary>
public sealed class OlxCaptureReaderTests
{
    private const string Ad = "https://www.olx.uz/d/oz/obyavlenie/desk-IDAb12.html";
    private const string Search = "https://www.olx.uz/oz/nedvizhimost/kvartiry/tashkent/?search%5Bfilter_float_price%3Ato%5D=10000000&q=desk";

    [Theory]
    [InlineData("http://www.olx.uz/oz/")]
    [InlineData("https://olx.uz.evil.test/oz/")]
    [InlineData("https://user:password@www.olx.uz/oz/")]
    [InlineData("https://127.0.0.1/")]
    [InlineData("https://www.olx.uz:8443/")]
    [InlineData("https://www.olx.uz/myaccount/")]
    [InlineData("https://www.olx.uz/api/offers/")]
    [InlineData("https://www.olx.uz/oz/chat/")]
    public void CanRead_ShouldRejectNonpublicRoutes(string url)
    {
        new OlxCaptureReader().CanRead(Document("<html />", url)).Should().BeFalse();
    }

    [Fact]
    public async Task Discover_ShouldDeduplicateIdsAcrossLocales_AndPreserveOmittedFilters()
    {
        const string html =
            """
            <div data-testid="listing-grid">
              <a href="/d/obyavlenie/desk-IDAb12.html?reason=promoted">Desk</a>
              <a href="/oz/d/elon/other-slug-IDAb12.html">Desk copy</a>
              <a href="https://evil.test/d/obyavlenie/desk-IDQ7.html">Other</a>
            </div>
            <a rel="next" href="?page=2">Next</a>
            """;

        CaptureReadResult read = Value(await new OlxCaptureReader().ReadAsync(Document(html, Search)));

        read.Discovered.Should().HaveCount(2);
        read.Discovered.Single(item => !item.IsListing).Url.Should().Be("https://www.olx.uz/d/obyavlenie/desk-IDAb12.html");
        string next = read.Discovered.Single(item => item.IsListing).Url;
        Uri.UnescapeDataString(next).Should().Contain("search[filter_float_price:to]=10000000").And.Contain("q=desk").And.Contain("page=2");
        read.Complete.Should().BeFalse();
    }

    [Theory]
    [InlineData("?page=1")]
    [InlineData("?page=0")]
    [InlineData("?page=2&page=3")]
    [InlineData("?page=2&q=car")]
    [InlineData("?page=2&search%5Bfilter_float_price%3Ato%5D=99999999")]
    [InlineData("/oz/transport/?page=2")]
    [InlineData("https://evil.test/oz/?page=2")]
    [InlineData("/d/obyavlenie/other-IDQ7.html")]
    public async Task Discover_ShouldRejectCyclesAndFilterOrHostChanges(string next)
    {
        string html = $"<div data-testid='listing-grid'><a href='{Ad}'>Desk</a></div><a rel='next' href='{next}'>Next</a>";

        CaptureReadResult read = Value(await new OlxCaptureReader().ReadAsync(Document(html, Search)));

        read.Discovered.Should().ContainSingle().Which.IsListing.Should().BeFalse();
    }

    [Fact]
    public async Task Discover_ShouldNotTreatAnUnrecognizedPageAsAnEmptySearch()
    {
        var reader = new OlxCaptureReader();

        (await reader.ReadAsync(Document("<h1>Site updated</h1>", Search))).IsSuccess.Should().BeFalse();
        CaptureReadResult empty = Value(await reader.ReadAsync(Document("<div data-testid='no-results'>No results</div><div data-testid='listing-grid'><a href='" + Ad + "'>Recommended ad outside the search</a></div>", Search)));

        empty.Discovered.Should().BeEmpty();
        empty.Complete.Should().BeFalse();
    }

    [Theory]
    [InlineData("/d/obyavlenie/desk-IDAb12.html")]
    [InlineData("/d/oz/obyavlenie/desk-IDAb12.html")]
    [InlineData("/d/ru/obyavlenie/desk-IDAb12.html")]
    [InlineData("/oz/d/elon/desk-IDAb12.html")]
    public async Task Read_ShouldUseTheAdIdAcrossLocaleAndSlugChanges(string path)
    {
        string url = "https://www.olx.uz" + path;
        Capture capture = Value(await new OlxCaptureReader().ReadAsync(Document(Visible(), url))).Captures.Single();

        capture.ExternalKey.Should().Be("Ab12");
        capture.OriginKey.Should().Be("olx.uz:ad:Ab12");
        capture.Url.Should().Be(url);
    }

    [Fact]
    public async Task Read_ShouldMapTheListingIntoPublicHarvest_AndRetainItsInitialFields()
    {
        string html = Visible() + Product() + Native("2026-10-08T21:26:22+05:00", "2026-10-26T05:01:46+05:00");
        var reader = new OlxCaptureReader();
        Capture capture = Value(await reader.ReadAsync(Document(html))).Captures.Single();
        PostEnvelope post = Value(await new RulesPostExtractor().ExtractAsync(capture, new() { Family = SourceFamily.Site }));

        capture.PublishedAtUtc.Should().Be(new DateTimeOffset(2026, 10, 8, 16, 26, 22, TimeSpan.Zero));
        post.Kind.Should().Be(PostKind.Listing);
        post.Title.Should().Be("Oak desk");
        post.Text.Should().Contain("Full description.\nSecond paragraph.");
        post.Prices.Should().ContainSingle().Which.Amount.Should().Be(700);
        post.Prices[0].Currency.Should().Be("USD");
        post.Prices[0].Role.Should().Be(PriceRole.Listed);
        post.Author!.Name.Should().Be("Public seller");
        post.Contacts.Should().ContainSingle().Which.Value.Should().Be("+998712051414");
        post.Extras["source"].GetProperty("fields").GetProperty("attributes").GetProperty("Condition").GetString().Should().Be("Used");
        post.Extras["source"].GetProperty("fields").GetProperty("lastRefreshTime").GetString().Should().Be("2026-10-26T05:01:46+05:00");
        post.Extras.ContainsKey("event").Should().BeFalse();
    }

    [Fact]
    public async Task Read_ShouldKeepOnePublishedPhotoPerCdnFile_AndExcludeRecommendations()
    {
        string html = Visible() +
            """
            <img data-testid="swiper-image" src="https://frankfurt.apollo.olxcdn.com/v1/files/desk-UZ/image;s=120x120">
            <meta property="og:image" content="https://frankfurt.apollo.olxcdn.com/v1/files/desk-UZ/image">
            <div data-testid="listing-grid"><img data-testid="swiper-image" src="https://frankfurt.apollo.olxcdn.com/v1/files/unrelated-UZ/image"></div>
            <img data-testid="swiper-image" src="https://evil.test/photo.jpg">
            """;

        Capture capture = Value(await new OlxCaptureReader().ReadAsync(Document(html))).Captures.Single();

        capture.Media.Should().ContainSingle().Which.Url.Should().Be("https://frankfurt.apollo.olxcdn.com/v1/files/desk-UZ/image");
    }

    [Fact]
    public async Task Read_ShouldPreservePartialDataAndLeaveRelativePublicationTimeUnknown()
    {
        Capture capture = Value(await new OlxCaptureReader().ReadAsync(Document("<div data-testid='offer_title'><h4>Desk</h4></div><span data-testid='ad-posted-at'>Today 10:00</span>"))).Captures.Single();

        capture.PublishedAtUtc.Should().BeNull();
        capture.Warnings.Should().Contain("publication_time_unresolved").And.Contain("price_not_extracted");
        capture.Structured[1].GetProperty("sourceFields").GetProperty("fields").GetProperty("postedAtText").GetString().Should().Be("Today 10:00");
    }

    [Fact]
    public async Task Read_ShouldNotTurnANegotiableZeroPlaceholderIntoAFreeOffer()
    {
        string html = Visible().Replace("700 USD", "Цена договорная", StringComparison.Ordinal)
            + Product().Replace("\"price\":700", "\"price\":0", StringComparison.Ordinal);
        Capture capture = Value(await new OlxCaptureReader().ReadAsync(Document(html))).Captures.Single();
        PostEnvelope post = Value(await new RulesPostExtractor().ExtractAsync(capture, new() { Family = SourceFamily.Site }));

        post.Prices.Should().BeEmpty();
        capture.Warnings.Should().Contain("price_not_extracted");
        post.Extras["source"].GetProperty("fields").GetProperty("originalProduct").GetProperty("offers").GetProperty("price").GetInt32().Should().Be(0);
    }

    [Fact]
    public async Task Read_ShouldExcludeRefreshAndExpiryTimesFromContentHashing()
    {
        var reader = new OlxCaptureReader();
        Capture first = Value(await reader.ReadAsync(Document(Visible() + Native("2026-10-08T21:26:22+05:00", "2026-10-26T05:01:46+05:00")))).Captures.Single();
        Capture second = Value(await reader.ReadAsync(Document(Visible() + Native("2026-10-08T21:26:22+05:00", "2026-10-27T05:01:46+05:00")))).Captures.Single();

        second.ContentProjection.Should().Be(first.ContentProjection);
    }

    [Theory]
    [InlineData("<div data-testid='ad-unavailable'>No longer available</div>")]
    [InlineData("<h1>Объявление неактивно</h1>")]
    [InlineData("<h2>E’lon mavjud emas</h2>")]
    public async Task Check_ShouldRecognizeExplicitUnavailableMarkers(string html)
    {
        LinkObservation? check = await new OlxCaptureReader().ReadAvailabilityAsync(Document(html), "Ab12");

        check.Should().NotBeNull();
        check!.Signal.Should().Be(LinkSignal.StrongNegative);
        check.Reason.Should().Be("soft_404");
    }

    [Theory]
    [InlineData("<title>Access denied</title>")]
    [InlineData("<title>Just a moment...</title><form id='challenge-form'></form>")]
    [InlineData("<h1>Unfamiliar page</h1>")]
    [InlineData("<div data-testid='listing-grid'><a href='/d/obyavlenie/other-IDQ7.html'>Related</a></div>")]
    public async Task Check_ShouldKeepUnknownAndChallengePagesInconclusive(string html)
    {
        (await new OlxCaptureReader().ReadAvailabilityAsync(Document(html), "Ab12")).Should().BeNull();
    }

    [Fact]
    public async Task Check_ShouldAcceptASameIdSlugRedirect_AndRejectAnUnrelatedLanding()
    {
        var reader = new OlxCaptureReader();
        FetchedDocument same = Document(Visible()) with { FinalUrl = new Uri("https://www.olx.uz/d/ru/obyavlenie/new-slug-IDAb12.html") };

        (await reader.ReadAvailabilityAsync(same, "Ab12"))!.Signal.Should().Be(LinkSignal.Present);
        (await reader.ReadAvailabilityAsync(same with { FinalUrl = new Uri("https://www.olx.uz/oz/") }, "Ab12")).Should().BeNull();
        FetchedDocument unrelatedMissing = Document("<div data-testid='ad-inactive'>Unavailable</div>") with
        {
            FinalUrl = new Uri("https://www.olx.uz/d/obyavlenie/other-IDQ7.html"), StatusCode = 404,
        };
        (await reader.ReadAvailabilityAsync(unrelatedMissing, "Ab12")).Should().BeNull();
        (await reader.ReadAvailabilityAsync(unrelatedMissing with { StatusCode = 200 }, "Ab12")).Should().BeNull();
        (await reader.ReadAvailabilityAsync(same, "Q7")).Should().BeNull();
    }

    [Fact]
    public async Task Check_ShouldRequireMatchingIdentityForInactiveHydration_AndLeaveConflictsUnresolved()
    {
        var reader = new OlxCaptureReader();
        string inactive = Native("2026-10-08T21:26:22+05:00", "2026-10-26T05:01:46+05:00", false);

        (await reader.ReadAvailabilityAsync(Document(inactive), "Ab12"))!.Signal.Should().Be(LinkSignal.StrongNegative);
        string conflict = Visible() + "<div data-testid='ad-unavailable'></div>" + Native("2026-10-08T21:26:22+05:00", "2026-10-26T05:01:46+05:00");
        (await reader.ReadAvailabilityAsync(Document(conflict), "Ab12")).Should().BeNull();
    }

    [Theory]
    [InlineData(404)]
    [InlineData(410)]
    public async Task Check_ShouldReturnDirectMissingEvidence(int status)
    {
        LinkObservation? result = await new OlxCaptureReader().ReadAvailabilityAsync(Document("") with { StatusCode = status }, "Ab12");

        result!.Reason.Should().Be("direct_" + status);
        result.HttpStatus.Should().Be(status);
    }

    [Fact]
    public async Task Read_ShouldRespectCancellation()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Func<Task> read = () => new OlxCaptureReader().ReadAsync(Document(Visible()), cancelled.Token);

        await read.Should().ThrowAsync<OperationCanceledException>();
    }

    private static string Visible()
    {
        return "<html lang='ru'><div data-testid='offer_title'><h4>Oak desk</h4></div>"
            + "<div data-testid='ad_description'><h3>Description</h3><div><p>Full description.</p><p>Second paragraph. Телефон (+998) 71−205−1414.</p></div></div>"
            + "<div data-testid='ad-price-container'><h3>700 USD</h3></div><h4 data-testid='user-profile-user-name'>Public seller</h4>"
            + "<div data-testid='ad-parameters-container'><p>Condition: Used</p></div></html>";
    }

    private static string Product()
    {
        return "<script type='application/ld+json'>" + JsonSerializer.Serialize(new
        {
            @type = "Product", name = "Structured title", description = "Structured fallback", url = Ad,
            offers = new { price = 700, priceCurrency = "USD", areaServed = new { name = "Tashkent" } },
        }).Replace("\"type\"", "\"@type\"", StringComparison.Ordinal) + "</script>";
    }

    private static string Native(string createdTime, string lastRefreshTime, bool isActive = true)
    {
        string state = JsonSerializer.Serialize(new { ad = new { ad = new { id = 1234, url = Ad, createdTime, lastRefreshTime, isActive, status = isActive ? "active" : "inactive" } } });
        return "<script>window.__PRERENDERED_STATE__= " + JsonSerializer.Serialize(state) + ";</script>";
    }

    private static FetchedDocument Document(string html, string url = Ad)
    {
        return new() { RequestUrl = new Uri(url), FinalUrl = new Uri(url), StatusCode = 200, ContentType = "text/html", Body = Encoding.UTF8.GetBytes(html), FetchedAtUtc = DateTimeOffset.UtcNow };
    }

    private static T Value<T>(Result<T> result) where T : notnull
    {
        return result is Result<T>.Success success ? success.Value
            : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);
    }
}
