using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Domtut;

namespace HarvestAdapters.Tests.Unit.Sources.Domtut;

/// <summary>Pins the ported Domtut behaviors over trimmed live pages and guards identity, routes and inconclusive
/// availability.</summary>
public sealed class DomtutCaptureReaderTests
{
    private const string Detail = "https://domtut.uz/nedvizhimost/zhiloj-kompleks-assalom-sohil";
    private const string Catalog = "https://domtut.uz/catalog-nedvijimosty";

    [Theory]
    [InlineData("http://domtut.uz/catalog-nedvijimosty")]
    [InlineData("https://domtut.uz.evil.test/catalog-nedvijimosty")]
    [InlineData("https://user:password@domtut.uz/catalog-nedvijimosty")]
    [InlineData("https://domtut.uz:8443/catalog-nedvijimosty")]
    [InlineData("https://www.olx.uz/nedvizhimost/zhiloj-kompleks-assalom-sohil")]
    [InlineData("https://domtut.uz/")]
    [InlineData("https://domtut.uz/404")]
    [InlineData("https://domtut.uz/filter/search?format=rent")]
    [InlineData("https://domtut.uz/agentstvo/golden-house")]
    [InlineData("https://domtut.uz/catalog-nedvijimosty?sort=price_a")]
    [InlineData("https://domtut.uz/catalog-nedvijimosty?min-price=1000")]
    public void Routes_ShouldRefuseWrongHostsJavaScriptSearchesAndRobotsDisallowedFilters(string url)
    {
        var reader = new DomtutCaptureReader();

        reader.Allows(new Uri(url)).Should().BeFalse();
        reader.CanRead(Document("<html />", url)).Should().BeFalse();
    }

    [Theory]
    [InlineData("https://domtut.uz/catalog-nedvijimosty?p=2")]
    [InlineData("https://www.domtut.uz/uz/catalog-nedvijimosty/rassrochka")]
    [InlineData("https://domtut.uz/comfort-class")]
    [InlineData("https://domtut.uz/uz/nedvizhimost/zhiloj-kompleks-assalom-sohil")]
    [InlineData("https://domtut.uz/nedvizhimost/352592-2komn-kv55")]
    public void Routes_ShouldAcceptTheServerRenderedCatalogAndListingPages(string url)
    {
        new DomtutCaptureReader().Allows(new Uri(url)).Should().BeTrue();
    }

    [Fact]
    public async Task Discover_ShouldReadOnlyCatalogCards_AndFollowTheNextPage()
    {
        CaptureReadResult read = Value(await new DomtutCaptureReader().ReadAsync(Document(DomtutPages.Catalog, Catalog)));

        read.Discovered.Where(item => !item.IsListing).Select(item => item.Url).Should().Equal(
            "https://domtut.uz/nedvizhimost/zhiloj-komleks-yunusabad-plaza", Detail);
        read.Discovered.Single(item => item.IsListing).Url.Should().Be("https://domtut.uz/catalog-nedvijimosty?p=2");
        read.Complete.Should().BeFalse();
    }

    [Fact]
    public async Task Discover_ShouldMoveForwardFromPageTwo_AndPreserveSavedFilters()
    {
        const string page = "https://domtut.uz/catalog-nedvijimosty/rassrochka?city=Tashkent&p=2";
        string html = "<div class='property-list'>" + DomtutPages.Cards + "</div><ul class='list-pages pagination'>"
            + "<li class='page-prev'><a href='/catalog-nedvijimosty/rassrochka?city=Tashkent&amp;p=1'>«</a></li>"
            + "<li class='page-next'><a href='/catalog-nedvijimosty/rassrochka?city=Tashkent&amp;p=3&amp;utm_source=tg'>»</a></li></ul>";

        CaptureReadResult read = Value(await new DomtutCaptureReader().ReadAsync(Document(html, page)));

        read.Discovered.Single(item => item.IsListing).Url.Should().Be("https://domtut.uz/catalog-nedvijimosty/rassrochka?city=Tashkent&p=3");
    }

    [Theory]
    [InlineData("/catalog-nedvijimosty?p=2")]
    [InlineData("/catalog-nedvijimosty?p=1")]
    [InlineData("/catalog-nedvijimosty?p=3&p=4")]
    [InlineData("/catalog-nedvijimosty?city=Samarkand&p=3")]
    [InlineData("/catalog-nedvijimosty?p=3&sort=price_a")]
    [InlineData("/catalog-nedvijimosty/ipoteka?p=3")]
    [InlineData("https://evil.test/catalog-nedvijimosty?p=3")]
    [InlineData("/nedvizhimost/zhiloj-kompleks-nest-one")]
    public async Task Discover_ShouldRejectCyclesAndFilterPathOrHostChanges(string next)
    {
        string html = $"<div class='property-list'>{DomtutPages.Cards}</div><ul class='pagination'><li class='page-next'><a href='{next}'>»</a></li></ul>";

        CaptureReadResult read = Value(await new DomtutCaptureReader().ReadAsync(Document(html, Catalog + "?p=2")));

        read.Discovered.Should().HaveCount(2).And.OnlyContain(item => !item.IsListing);
    }

    [Fact]
    public async Task Discover_ShouldNotTreatAnUnrecognizedPageAsAnEmptyCatalog()
    {
        var reader = new DomtutCaptureReader();

        (await reader.ReadAsync(Document("<h1>Site updated</h1>", Catalog))).IsSuccess.Should().BeFalse();
        (await reader.ReadAsync(Document("<main class='filter-page new-filter'><filters :mode=\"'plans'\"></filters></main>", Catalog))).IsSuccess.Should().BeFalse();
        CaptureReadResult empty = Value(await reader.ReadAsync(Document(
            "<div class='catalog-list'><div class='no-results'>Ничего не найдено</div></div><div class='property-list'>" + DomtutPages.Cards + "</div>", Catalog)));

        empty.Discovered.Should().BeEmpty();
        empty.Complete.Should().BeFalse();
    }

    [Fact]
    public async Task Read_ShouldMapTheComplexIntoAListingPost_AndRetainItsSourceFields()
    {
        Capture capture = Value(await new DomtutCaptureReader().ReadAsync(Document(DomtutPages.Detail))).Captures.Single();
        PostEnvelope post = Value(await new RulesPostExtractor().ExtractAsync(capture, new() { Family = SourceFamily.Site }));

        capture.ExternalKey.Should().Be("7");
        capture.OriginKey.Should().Be("domtut.uz:ad:7");
        capture.Url.Should().Be(Detail);
        capture.Locale.Should().Be("ru");
        capture.PublishedAtUtc.Should().BeNull();
        capture.Warnings.Should().Equal("publication_time_unresolved");
        capture.Links.Should().ContainSingle().Which.Url.Should().StartWith("https://gh.uz/sohil/");
        post.Kind.Should().Be(PostKind.Listing);
        post.Title.Should().Be("ЖК «Assalom Sohil»");
        post.Text.Should().StartWith("Жилой комплекс «Assalom Sohil»\nКомплекс из 13 корпусов переменной этажности в Яшнабадском районе.\nИнфраструктура");
        PostPrice price = post.Prices.Should().ContainSingle().Subject;
        price.Min.Should().Be(1262976300);
        price.Amount.Should().BeNull();
        price.Currency.Should().Be("UZS");
        price.Role.Should().Be(PriceRole.Listed);
        post.Author!.Name.Should().Be("Golden House");
        post.Location!.Mentions.Select(mention => mention.Text).Should().Contain(["Ташкент", "Яшнабадский", "ул Фаргона йули, д. 6"]);
        post.Location.Geo!.Latitude.Should().BeApproximately(41.28287003, 1e-8);
        post.Contacts.Should().BeEmpty();
        post.Extras.ContainsKey("event").Should().BeFalse();
        post.Extras["source"].GetProperty("schema").GetString().Should().Be("domtut.ad.v1");
        JsonElement fields = post.Extras["source"].GetProperty("fields");
        fields.GetProperty("externalId").GetString().Should().Be("7");
        fields.GetProperty("slug").GetString().Should().Be("zhiloj-kompleks-assalom-sohil");
        fields.GetProperty("districtKey").GetString().Should().Be("yashnobod");
        fields.GetProperty("priceText").GetString().Should().Be("от 1 262 976 300 сум");
        fields.GetProperty("pricePerMeterText").GetString().Should().Be("18 864 471 сум за м²");
        fields.GetProperty("updatedText").GetString().Should().Be("Обновлено 12.11.2026");
        fields.GetProperty("status").GetString().Should().Be("publish");
        fields.GetProperty("attributes").GetProperty("Этажность").GetString().Should().Be("9");
        fields.GetProperty("attributes").GetProperty("Лифт").GetString().Should().Be("4");
        fields.GetProperty("attributes").GetProperty("Метро").GetString().Should().Be("Технопарк на транспорте 1.8 км 7 мин");
        JsonElement layout = fields.GetProperty("layouts").EnumerateArray().Single();
        layout.GetProperty("rooms").GetString().Should().Be("3");
        layout.GetProperty("area").GetString().Should().Be("от 67 м²");
        layout.GetProperty("price").GetString().Should().Be("1262976300");
        fields.GetProperty("labels").EnumerateArray().Select(label => label.GetString()).Should().Equal("Ипотека 0%", "Готов к заселению", "Скидка до 63148816 сум");
        fields.GetProperty("originalProduct").GetProperty("offers").GetProperty("lowPrice").GetInt64().Should().Be(1262976300);
        string structured = JsonSerializer.Serialize(capture.Structured) + capture.ContentProjection;
        structured.Should().NotContain("Reviewer-one").And.NotContain("99800000").And.NotContain("352592");
    }

    [Fact]
    public async Task Read_ShouldKeepOneFullPhotoPerFile_AndExcludeVideosPlansAndOtherListings()
    {
        Capture capture = Value(await new DomtutCaptureReader().ReadAsync(Document(DomtutPages.Detail))).Captures.Single();

        capture.Media.Select(item => item.Url).Should().Equal(
            "https://domtut.uz/resources/uploads/property/zhiloj-kompleks-assalom-sohil/main_0.jpg",
            "https://domtut.uz/resources/uploads/property/zhiloj-kompleks-assalom-sohil/main_1.jpg");
    }

    [Theory]
    [InlineData("https://domtut.uz/uz/nedvizhimost/zhiloj-kompleks-assalom-sohil")]
    [InlineData("https://www.domtut.uz/nedvizhimost/assalom-sohil-renamed")]
    public async Task Read_ShouldKeyThePostByItsNumericIdAcrossLocaleAndSlugChanges(string url)
    {
        Capture capture = Value(await new DomtutCaptureReader().ReadAsync(Document(DomtutPages.Detail, url))).Captures.Single();

        capture.ExternalKey.Should().Be("7");
        capture.OriginKey.Should().Be("domtut.uz:ad:7");
    }

    [Fact]
    public async Task Read_ShouldTreatPriceOnRequestAsNoPrice()
    {
        string html = DomtutPages.Detail.Replace("<div class=\"price-block \"", "<div class=\"price-block price-negotiate\"", StringComparison.Ordinal);
        Capture capture = Value(await new DomtutCaptureReader().ReadAsync(Document(html))).Captures.Single();
        PostEnvelope post = Value(await new RulesPostExtractor().ExtractAsync(capture, new() { Family = SourceFamily.Site }));

        post.Prices.Should().BeEmpty();
        capture.Warnings.Should().Contain("price_not_extracted");
        post.Extras["source"].GetProperty("fields").GetProperty("priceText").GetString().Should().Be("Цена по запросу");
        post.Extras["source"].GetProperty("fields").GetProperty("priceNegotiable").GetBoolean().Should().BeTrue();
    }

    [Fact]
    public async Task Read_ShouldExcludeTheUpdatedStampAndOfferValidityFromContentHashing()
    {
        var reader = new DomtutCaptureReader();
        string later = DomtutPages.Detail.Replace("Обновлено 12.11.2026", "Обновлено 13.11.2026", StringComparison.Ordinal)
            .Replace("2026-11-13", "2026-11-14", StringComparison.Ordinal);
        Capture first = Value(await reader.ReadAsync(Document(DomtutPages.Detail))).Captures.Single();
        Capture second = Value(await reader.ReadAsync(Document(later))).Captures.Single();

        second.ContentProjection.Should().Be(first.ContentProjection);
    }

    [Fact]
    public async Task Read_ShouldRefuseRedirectsAwayFromTheListing_MissingIdsAndChallenges()
    {
        var reader = new DomtutCaptureReader();

        (await reader.ReadAsync(Document(DomtutPages.Detail) with { FinalUrl = new Uri(Catalog) })).IsSuccess.Should().BeFalse();
        (await reader.ReadAsync(Document(DomtutPages.Detail.Replace(" data-id=\"7\"", string.Empty, StringComparison.Ordinal)))).IsSuccess.Should().BeFalse();
        (await reader.ReadAsync(Document("<title>Just a moment...</title><form id='challenge-form'></form>"))).IsSuccess.Should().BeFalse();
        (await reader.ReadAsync(Document("<h1>Страница не найдена</h1>"))).IsSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData("<title>404 | Domtut</title><h1>404</h1>")]
    [InlineData("<h1>Страница не найдена</h1>")]
    [InlineData("<h2>Sahifa topilmadi</h2>")]
    public async Task Check_ShouldRecognizeExplicitNotFoundPages(string html)
    {
        LinkObservation? check = await new DomtutCaptureReader().ReadAvailabilityAsync(Document(html), "7");

        check.Should().NotBeNull();
        check!.Signal.Should().Be(LinkSignal.StrongNegative);
        check.Reason.Should().Be("soft_404");
    }

    [Fact]
    public async Task Check_ShouldTreatANonPublishedStatusOfTheSameIdAsRemoved()
    {
        var reader = new DomtutCaptureReader();
        FetchedDocument trashed = Document(DomtutPages.Detail.Replace("status-publish", "status-trash", StringComparison.Ordinal));

        (await reader.ReadAvailabilityAsync(trashed, "7"))!.Signal.Should().Be(LinkSignal.StrongNegative);
        (await reader.ReadAvailabilityAsync(trashed, "863")).Should().BeNull();
        (await reader.ReadAsync(trashed)).IsSuccess.Should().BeFalse();
    }

    [Theory]
    [InlineData("<title>Access denied</title>")]
    [InlineData("<title>Just a moment...</title><form id='challenge-form'></form>")]
    [InlineData("<h1>Unfamiliar page</h1>")]
    [InlineData("<div class='property-list'><article class='property-card'><a href='/nedvizhimost/other'>Related</a></article></div>")]
    public async Task Check_ShouldKeepUnknownAndChallengePagesInconclusive(string html)
    {
        (await new DomtutCaptureReader().ReadAvailabilityAsync(Document(html), "7")).Should().BeNull();
    }

    [Fact]
    public async Task Check_ShouldConfirmTheSameIdAfterASlugRedirect_AndRejectUnrelatedLandings()
    {
        var reader = new DomtutCaptureReader();
        FetchedDocument moved = Document(DomtutPages.Detail, "https://domtut.uz/nedvizhimost/assalom-sohil") with { FinalUrl = new Uri(Detail) };

        (await reader.ReadAvailabilityAsync(moved, "7"))!.Signal.Should().Be(LinkSignal.Present);
        (await reader.ReadAvailabilityAsync(moved, "863")).Should().BeNull();
        (await reader.ReadAvailabilityAsync(moved, Detail)).Should().BeNull();
        (await reader.ReadAvailabilityAsync(moved with { FinalUrl = new Uri(Catalog) }, "7")).Should().BeNull();
        (await reader.ReadAvailabilityAsync(Document("<h1>Страница не найдена</h1>") with { FinalUrl = new Uri("https://domtut.uz/nedvizhimost/other") }, "7")).Should().BeNull();
        (await reader.ReadAvailabilityAsync(Document(string.Empty) with { FinalUrl = new Uri("https://domtut.uz/nedvizhimost/other"), StatusCode = 404 }, "7")).Should().BeNull();
        (await reader.ReadAvailabilityAsync(Document(DomtutPages.Detail, Catalog), "7")).Should().BeNull();
    }

    [Theory]
    [InlineData(404)]
    [InlineData(410)]
    public async Task Check_ShouldReturnDirectMissingEvidence(int status)
    {
        LinkObservation? result = await new DomtutCaptureReader().ReadAvailabilityAsync(Document(string.Empty) with { StatusCode = status }, "7");

        result!.Signal.Should().Be(LinkSignal.StrongNegative);
        result.Reason.Should().Be("direct_" + status);
        result.HttpStatus.Should().Be(status);
    }

    [Fact]
    public async Task Read_ShouldRespectCancellation()
    {
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        Func<Task> read = () => new DomtutCaptureReader().ReadAsync(Document(DomtutPages.Detail), cancelled.Token);

        await read.Should().ThrowAsync<OperationCanceledException>();
    }

    private static FetchedDocument Document(string html, string url = Detail)
    {
        return new() { RequestUrl = new Uri(url), FinalUrl = new Uri(url), StatusCode = 200, ContentType = "text/html", Body = Encoding.UTF8.GetBytes(html), FetchedAtUtc = DateTimeOffset.UtcNow };
    }

    private static T Value<T>(Result<T> result) where T : notnull
    {
        return result is Result<T>.Success success ? success.Value
            : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);
    }
}
