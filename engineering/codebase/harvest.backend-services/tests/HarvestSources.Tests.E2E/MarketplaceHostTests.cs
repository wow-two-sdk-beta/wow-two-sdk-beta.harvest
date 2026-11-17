using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using HarvestSources.Api.Configurations;
using HarvestSources.Api.Marketplace;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.BirBir;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Joymee;

namespace HarvestSources.Tests.E2E;

/// <summary>Provisions a marketplace profile over HTTP and pulls its listings with the consumer's own key, in the shape
/// Haven Supply's <c>HarvestOlxClient</c> parses.</summary>
public sealed class MarketplaceHostTests
{
    private const string OperatorKey = "hv_MarketplaceHostTestOperator00000";
    private const string BirBirListingUrl = BirBirSourceAdapter.RentalCatalog + "/o/fixture-apartment-1842";

    [Fact]
    public async Task Consumer_key_pulls_olx_listings_with_their_source_fields()
    {
        var fetcher = new OlxFixtureFetcher();
        await using var host = await StartAsync(fetcher);
        using HttpClient operatorClient = host.GetTestClient();
        (await operatorClient.PostAsJsonAsync("/api/marketplace/haven/setup", new { })).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        operatorClient.DefaultRequestHeaders.Add("X-Api-Key", OperatorKey);
        (await Data(await operatorClient.GetAsync("/api/marketplace/profiles"))).EnumerateArray()
            .Select(profile => profile.GetProperty("key").GetString()).Should().Contain("haven");
        (await operatorClient.PostAsJsonAsync("/api/marketplace/nowhere/setup", new { })).StatusCode.Should().Be(HttpStatusCode.NotFound);

        var setup = await Data(await operatorClient.PostAsJsonAsync("/api/marketplace/haven/setup", new { issueKey = true }));
        string secret = setup.GetProperty("consumerKey").GetProperty("secret").GetString()!;
        var apartments = setup.GetProperty("searches").EnumerateArray().First(search => search.GetProperty("key").GetString() == "apartments-rent");
        var repeated = await Data(await operatorClient.PostAsJsonAsync("/api/marketplace/haven/setup", new { issueKey = true }));
        repeated.GetProperty("consumerKey").TryGetProperty("secret", out _).Should().BeFalse("a secret is returned once");
        fetcher.Calls.Should().BeEmpty("setup reads nothing from the source");

        await RunAsync(operatorClient, host.Services, apartments.GetProperty("pipelineId").GetString()!);

        var sourceId = apartments.GetProperty("sourceId").GetGuid();
        var filtered = await Data(await operatorClient.PostAsJsonAsync($"/api/sources/{sourceId}/posts/query",
            new { filters = new { rooms = "2" } }));
        filtered.GetProperty("items").EnumerateArray().Single().GetProperty("listing").GetProperty("rooms").GetInt32().Should().Be(2);
        filtered.GetProperty("facets").EnumerateArray().Single(facet => facet.GetProperty("key").GetString() == "rooms")
            .GetProperty("options").EnumerateArray().Should().Contain(option =>
                option.GetProperty("value").GetString() == "3" && option.GetProperty("count").GetInt32() == 1);

        using HttpClient haven = host.GetTestClient();
        haven.DefaultRequestHeaders.Add("X-Api-Key", secret);
        (await haven.PostAsJsonAsync("/api/marketplace/haven/setup", new { })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var page = await PullAsync(haven, $"/api/subscriptions/{apartments.GetProperty("subscriptionId").GetString()}/events?limit=50");
        page.GetProperty("next").GetString().Should().NotBeNullOrEmpty();
        var deliveries = page.GetProperty("data").EnumerateArray().ToArray();
        deliveries.Should().HaveCount(2).And.OnlyContain(item => item.GetProperty("type").GetString() == "post.new");
        foreach (var delivery in deliveries)
        {
            delivery.GetProperty("context").GetProperty("catalog").GetString().Should().Be("haven-rent-res");
            delivery.GetProperty("context").GetProperty("search").GetString().Should().Be("apartments-rent");
            var post = delivery.GetProperty("data");
            post.GetProperty("base").GetProperty("kind").GetString().Should().Be("listing");
            var link = post.GetProperty("links").EnumerateArray().Single();
            link.GetProperty("url").GetString().Should().StartWith("https://www.olx.uz/d/obyavlenie/");
            link.GetProperty("externalKey").GetString().Should().BeOneOf("Ab12", "Cd34");
            link.GetProperty("captureId").GetGuid().Should().NotBeEmpty();
            link.GetProperty("capturedAtUtc").GetDateTimeOffset().Should().BeAfter(DateTimeOffset.UtcNow.AddHours(-1));
            link.GetProperty("sourceFields").GetProperty("schema").GetString().Should().Be("olx.ad.v1");
            link.GetProperty("sourceFields").GetProperty("fields").GetProperty("title").GetString().Should().Contain("квартира");
        }

        using var output = new StringWriter();
        using var error = new StringWriter();
        (await MarketplaceSetupVerb.RunAsync(host.Services, ["status", "--profile", "haven"], output, error, CancellationToken.None))
            .Should().Be(0, error.ToString());
        var readout = JsonDocument.Parse(output.ToString().TrimEnd().Split('\n')[^1]).RootElement;
        readout.GetProperty("pipelines").EnumerateArray().Select(line => line.GetString()).Should().Contain(line =>
            line!.StartsWith("apartments-rent ") && line.Contains(" 2 captured 0 failed ") && line.EndsWith("; events 2"),
            "the deployed host's status proves delivery without the consumer's key");
        readout.GetProperty("hosts").EnumerateArray().Select(host => host.GetString()).Should().Contain(host => host!.StartsWith("www.olx.uz/"));
    }

    [Theory]
    [InlineData("birbir", "birbir.ad.v1", "https://birbir.uz/ru/tashkent/cat/nedvizhimost/arenda/kvartiry/o/test-1842")]
    [InlineData("domtut", "domtut.rental.v1", "https://domtut.uz/nedvizhimost/test")]
    public async Task Operator_import_delivers_one_listing_and_does_not_fetch_its_url(string site, string schema, string url)
    {
        var fetcher = new OlxFixtureFetcher();
        await using var host = await StartAsync(fetcher);
        using HttpClient operatorClient = host.GetTestClient();
        operatorClient.DefaultRequestHeaders.Add("X-Api-Key", OperatorKey);
        var setup = await Data(await operatorClient.PostAsJsonAsync("/api/marketplace/haven/setup", new { issueKey = true }));
        string searchKey = site + "-apartments-rent";
        var search = setup.GetProperty("searches").EnumerateArray().Single(row => row.GetProperty("key").GetString() == searchKey);
        search.GetProperty("importOnly").GetBoolean().Should().BeTrue();
        var path = $"/api/marketplace/haven/searches/{searchKey}/listings";
        var listing = new { externalId = "1842", url, title = "Imported test apartment", priceText = "450 USD",
            description = "A fixture apartment", city = "Tashkent", district = "Mirobod" };
        using HttpClient haven = host.GetTestClient();
        haven.DefaultRequestHeaders.Add("X-Api-Key", setup.GetProperty("consumerKey").GetProperty("secret").GetString()!);
        (await haven.PostAsJsonAsync(path, listing)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await operatorClient.PostAsJsonAsync(path, listing with { url = "https://example.test/1842" }))
            .StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var rejectedRun = await operatorClient.PostAsJsonAsync("/api/runs", new { pipelineId = search.GetProperty("pipelineId").GetGuid() });
        rejectedRun.IsSuccessStatusCode.Should().BeFalse();

        await Data(await operatorClient.PostAsJsonAsync(path, listing));
        await Data(await operatorClient.PostAsJsonAsync(path, listing));
        fetcher.Calls.Should().BeEmpty();

        var page = await PullAsync(haven, $"/api/subscriptions/{search.GetProperty("subscriptionId").GetGuid()}/events?limit=50", expected: 1);
        var delivery = page.GetProperty("data").EnumerateArray().ToArray().Should().ContainSingle().Subject;
        var link = delivery.GetProperty("data").GetProperty("links").EnumerateArray().Single();
        link.GetProperty("externalKey").GetString().Should().Be("1842");
        link.GetProperty("sourceFields").GetProperty("schema").GetString().Should().Be(schema);
        link.GetProperty("sourceFields").GetProperty("fields").GetProperty("priceText").GetString().Should().Be("450 USD");
    }

    [Fact]
    public async Task BirBir_registered_schema_matches_the_collected_feed_and_default_setup_stays_idle()
    {
        var fetcher = new BirBirFixtureFetcher();
        await using var host = await StartAsync(fetcher);
        using HttpClient admin = host.GetTestClient();
        admin.DefaultRequestHeaders.Add("X-Api-Key", OperatorKey);
        var setup = await Data(await admin.PostAsJsonAsync("/api/marketplace/haven/setup", new { issueKey = true }));
        var searches = setup.GetProperty("searches").EnumerateArray().ToArray();
        var collection = searches.Single(row => row.GetProperty("key").GetString() == "birbir-collection");
        collection.GetProperty("canCapture").GetBoolean().Should().BeTrue();
        fetcher.Requests.Should().BeEmpty();

        var sourceId = searches.Single(row => row.GetProperty("key").GetString() == "birbir-apartments-rent").GetProperty("sourceId").GetGuid();
        var capabilities = await Data(await admin.GetAsync($"/api/sources/{sourceId}/capabilities"));
        capabilities.GetProperty("configuration").GetProperty("readiness").GetString().Should().Be("configurationRequired");
        capabilities.GetProperty("workKinds").GetArrayLength().Should().Be(0);
        var registrySchema = capabilities.GetProperty("configuration").GetProperty("sourceFieldsSchema").GetProperty("properties");
        registrySchema.GetProperty("schema").GetProperty("const").GetString().Should().Be("birbir.ad.v1");

        var pipelineId = collection.GetProperty("pipelineId").GetGuid();
        var subscriptionId = collection.GetProperty("subscriptionId").GetGuid();

        var stagesUrl = $"/api/pipelines/{pipelineId}/stages";
        var stages = await Data(await admin.GetAsync(stagesUrl));
        stages.EnumerateArray().Select(stage => stage.GetProperty("kind").GetString()).Should()
            .Equal("discover", "harvest", "process");
        (await Data(await admin.PutAsJsonAsync(stagesUrl, new { kind = "harvest", mode = "manual", enabled = false,
            stage = new { batchSize = 1 } }))).GetBoolean().Should().BeTrue();
        await RunAsync(admin, host.Services, pipelineId.ToString(), "discover");
        var discoveries = await Data(await admin.GetAsync($"/api/pipelines/{pipelineId}/discoveries?state=pending"));
        discoveries.EnumerateArray().Single().GetProperty("externalKey").GetString().Should().Be("1842");
        await RunAsync(admin, host.Services, pipelineId.ToString(), "harvest");
        using HttpClient haven = host.GetTestClient();
        haven.DefaultRequestHeaders.Add("X-Api-Key", setup.GetProperty("consumerKey").GetProperty("secret").GetString()!);
        var page = await PullAsync(haven, $"/api/subscriptions/{subscriptionId}/events?limit=50", expected: 1);
        var post = page.GetProperty("data").EnumerateArray().Single().GetProperty("data");
        var link = post.GetProperty("links").EnumerateArray().Single();
        link.GetProperty("externalKey").GetString().Should().Be("1842");
        post.GetProperty("base").GetProperty("kind").GetString().Should().Be("listing");
        var envelope = link.GetProperty("sourceFields");
        envelope.GetProperty("schema").GetString().Should().Be("birbir.ad.v1");
        var fields = envelope.GetProperty("fields");
        var declaredFields = registrySchema.GetProperty("fields");
        fields.EnumerateObject().Select(p => p.Name).Should().BeEquivalentTo(
            declaredFields.GetProperty("properties").EnumerateObject().Select(p => p.Name));
        declaredFields.GetProperty("additionalProperties").GetBoolean().Should().BeFalse();
        declaredFields.GetProperty("properties").GetProperty("title").GetProperty("maxLength").GetInt32().Should().Be(500);
        fields.GetProperty("priceAmount").GetDecimal().Should().Be(450.75m);
        fields.GetProperty("currency").GetString().Should().Be("USD");
        fields.GetProperty("areaSquareMetres").GetDecimal().Should().Be(74.5m);
        fields.GetProperty("roomCount").GetInt32().Should().Be(3);
        fields.GetProperty("floor").ValueKind.Should().Be(JsonValueKind.Null);
        fields.GetProperty("buildingFloors").GetInt32().Should().Be(9);
        fields.GetProperty("district").GetString().Should().Be("Мирабадский район");
        fields.GetProperty("publishedAtUtc").GetDateTimeOffset().Should().Be(new DateTimeOffset(2026, 11, 14, 7, 8, 0, TimeSpan.Zero));
        fields.GetProperty("attributes").GetProperty("Ремонт").GetString().Should().Be("Евро");
        fields.GetProperty("imageUrls").GetArrayLength().Should().Be(1);
        fetcher.Requests.Should().Equal(BirBirSourceAdapter.RentalCatalog, BirBirListingUrl);
    }

    [Fact]
    public async Task Joymee_capture_delivers_only_matching_rentals_with_the_public_listing_url()
    {
        await using var host = await StartAsync(new JoymeeFixtureFetcher());
        using HttpClient client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", OperatorKey);
        var setup = await Data(await client.PostAsJsonAsync("/api/marketplace/haven/setup", new { }));
        var search = setup.GetProperty("searches").EnumerateArray().Single(row => row.GetProperty("key").GetString() == "joymee-apartments-rent");
        await RunAsync(client, host.Services, search.GetProperty("pipelineId").GetString()!);

        var page = await PullAsync(client, $"/api/subscriptions/{search.GetProperty("subscriptionId").GetGuid()}/events?limit=50", expected: 1);
        var delivery = page.GetProperty("data").EnumerateArray().ToArray().Should().ContainSingle().Subject;
        var link = delivery.GetProperty("data").GetProperty("links").EnumerateArray().Single();
        link.GetProperty("url").GetString().Should().Be("https://joymee.uz/announcements/1842");
        link.GetProperty("sourceFields").GetProperty("schema").GetString().Should().Be("joymee.ad.v1");
        link.GetProperty("sourceFields").GetProperty("fields").GetProperty("priceText").GetString().Should().Be("450.00 USD");

        string pipelineId = search.GetProperty("pipelineId").GetString()!;
        var processing = await RunAsync(client, host.Services, pipelineId, "process");
        processing.GetProperty("captured").GetInt32().Should().Be(1);
        processing.GetProperty("requests").GetInt32().Should().Be(0);
        var attempts = await Data(await client.GetAsync($"/api/runs/{processing.GetProperty("id").GetString()}/audit"));
        attempts.EnumerateArray().Single().GetProperty("target").GetString().Should().Be("https://joymee.uz/announcements/1842");
        var replay = await RunAsync(client, host.Services, pipelineId, "process");
        replay.GetProperty("completionReason").GetString().Should().Be("noItems");
    }

    private static async Task<WebApplication> StartAsync(IPageFetcher fetcher)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = "Development",
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DatabaseSettings:ConnectionString"] = await SourcePostgres.NewDatabaseAsync(),
            ["Harvest:Operator:ApiKey"] = OperatorKey,
            ["Harvest:Worker:Enabled"] = "false",
            ["AllowedHosts"] = "localhost",
            ["Logging:LogLevel:Default"] = "Warning",
        });
        builder.WebHost.UseTestServer();
        builder.Configure();
        builder.Services.RemoveAll<IPageFetcher>();
        builder.Services.AddSingleton<IPageFetcher>(fetcher);
        var host = builder.Build();
        await host.ConfigureAsync();
        await host.StartAsync();
        return host;
    }

    /// <summary>Queues one capture and works it to completion; OLX's step pace spaces the reads, so idle turns wait briefly.</summary>
    private static async Task<JsonElement> RunAsync(HttpClient client, IServiceProvider services, string pipelineId, string kind = "capture")
    {
        var run = await Data(await client.PostAsJsonAsync("/api/runs", new { pipelineId, kind }));
        string runUrl = $"/api/runs/{run.GetProperty("id").GetString()}";
        await using var scope = services.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<HarvestEngine>();
        var deadline = DateTimeOffset.UtcNow.AddSeconds(45);
        while (DateTimeOffset.UtcNow < deadline)
        {
            if (await engine.ProcessNextAsync())
                continue;
            var current = await Data(await client.GetAsync(runUrl));
            string status = current.GetProperty("status").GetString()!;
            if (status is "completed" or "failed" or "partial" or "cancelled")
            {
                status.Should().Be("completed", current.GetRawText());
                return current;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
        throw new TimeoutException("The marketplace fixture capture did not finish.");
    }

    private static async Task<JsonElement> PullAsync(HttpClient client, string url, int expected = 2)
    {
        // A pull reads below the oldest open transaction, so a concurrent one can briefly hide a committed event.
        for (var attempt = 1; ; attempt++)
        {
            var page = await Data(await client.GetAsync(url));
            if (page.GetProperty("data").GetArrayLength() >= expected || attempt == 20)
                return page;
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
    }

    private static async Task<JsonElement> Data(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(body);
        using var parsed = JsonDocument.Parse(body);
        return parsed.RootElement.GetProperty("data").Clone();
    }

    private sealed class BirBirFixtureFetcher : IPageFetcher
    {
        public List<string> Requests { get; } = [];

        public Task<FetchResponse> FetchAsync(FetchRequest request, CancellationToken cancellationToken = default)
        {
            var url = request.Uri.AbsoluteUri;
            Requests.Add(url);
            request.RobotsPolicy.Should().Be(RobotsPolicy.Obey);
            request.DestinationPolicy!.Allows(new Uri(BirBirSourceAdapter.RentalCatalog + "?page=2")).Should().BeFalse();
            var body = url switch
            {
                BirBirSourceAdapter.RentalCatalog => $"<main><div data-testid='announcements_list'><div data-testid='feed_item-1'><a href='{BirBirListingUrl}'>Rental</a></div></div></main>",
                BirBirListingUrl => """"
                    <html lang="ru"><head><script type="application/ld+json">
                    {"@type":"Product","sku":1842,"name":"Квартира рядом с метро","description":"Этаж 4 упомянут только в описании.",
                     "image":["https://img.birbir.uz/fixture.jpg","https://other.test/img.jpg"],
                     "offers":{"url":"__LISTING_URL__","price":450.75,"priceCurrency":"USD","validFrom":"2026-11-14",
                               "availability":"https://schema.org/InStock","itemCondition":"https://schema.org/UsedCondition",
                               "availableAtOrFrom":{"address":{"addressLocality":"Ташкент","addressCountry":"UZ"}}},
                     "additionalProperty":[{"name":"Площадь","value":"74,5"},{"name":"Комнаты","value":"3-комнатная"},
                      {"name":"Этажность здания","value":"9"},{"name":"Ремонт","value":"Евро"}]}
                    </script></head><body><main>
                    <h1 data-testid="offer-details_title">Квартира рядом с метро</h1>
                    <p data-testid="offer-details_price">450,75 у.е.</p>
                    <div data-testid="offer_delivery_address"><div><p>Узбекистан, Ташкент, Мирабадский район</p><p>Мирабадский район</p></div></div>
                    <div data-testid="offer_number"><p>№ объявления</p><p>1842</p></div>
                    <div data-testid="offer_time"><p>Размещено</p><p>14.11.2026, 12:08</p></div>
                    </main></body></html>
                    """".Replace("__LISTING_URL__", BirBirListingUrl, StringComparison.Ordinal),
                _ => throw new InvalidOperationException($"Unexpected fixture fetch: {url}"),
            };
            var bytes = Encoding.UTF8.GetBytes(body);
            return Task.FromResult(new FetchResponse
            {
                Outcome = FetchOutcome.Ok,
                Document = new()
                {
                    RequestUrl = request.Uri, FinalUrl = request.Uri, StatusCode = 200, ContentType = "text/html",
                    Body = bytes, FetchedAtUtc = DateTimeOffset.UtcNow,
                },
                Usage = new() { Requests = 1, Bytes = bytes.Length },
            });
        }
    }

    private sealed class JoymeeFixtureFetcher : IPageFetcher
    {
        public Task<FetchResponse> FetchAsync(FetchRequest request, CancellationToken cancellationToken = default)
        {
            request.Uri.IdnHost.Should().Be(JoymeeSourceAdapter.Host);
            request.RobotsPolicy.Should().Be(RobotsPolicy.Obey);
            var body = request.Uri.AbsolutePath switch
            {
                "/api/v1/announcement/" => """{"page":1,"results":[{"id":1842},{"id":1843}],"next":null}""",
                "/api/v1/announcement/1842/" => """
                    {"id":1842,"title":"Test apartment","description":"A fixture apartment","status":1,
                     "deal_type":2,"property_type":1,"region":{"id":59,"name":"Toshkent"},
                     "pricing":{"price":"450.00","currency":2},"detail":{"room_quantity":2}}
                    """,
                "/api/v1/announcement/1843/" => """
                    {"id":1843,"title":"Apartment for sale","status":1,"deal_type":3,"property_type":1,"region":{"id":59}}
                    """,
                _ => throw new InvalidOperationException("Unexpected Joymee fixture URL."),
            };
            byte[] bytes = Encoding.UTF8.GetBytes(body);
            return Task.FromResult(new FetchResponse
            {
                Outcome = FetchOutcome.Ok,
                Usage = new() { Requests = 1, Bytes = bytes.Length },
                Document = new()
                {
                    RequestUrl = request.Uri, FinalUrl = request.Uri, StatusCode = 200,
                    ContentType = "application/json", Body = bytes, FetchedAtUtc = DateTimeOffset.UtcNow,
                },
            });
        }
    }

    private sealed class OlxFixtureFetcher : IPageFetcher
    {
        internal List<Uri> Calls { get; } = [];

        public Task<FetchResponse> FetchAsync(FetchRequest request, CancellationToken cancellationToken = default)
        {
            lock (Calls)
                Calls.Add(request.Uri);
            request.RobotsPolicy.Should().Be(RobotsPolicy.Obey);
            string html;
            if (!request.Uri.AbsolutePath.Contains("/d/", StringComparison.Ordinal))
            {
                request.Uri.AbsolutePath.Should().Be("/nedvizhimost/kvartiry/arenda-dolgosrochnaya/tashkent/");
                html = "<div data-testid='listing-grid'>"
                    + "<a href='/d/obyavlenie/2-komnatnaya-kvartira-IDAb12.html'>2 комн</a>"
                    + "<a href='/d/obyavlenie/3-komnatnaya-kvartira-IDCd34.html'>3 комн</a></div>";
            }
            else
            {
                string id = request.Uri.AbsolutePath.Contains("IDAb12", StringComparison.Ordinal) ? "Ab12" : "Cd34";
                html = $"<div data-testid='offer_title'><h4>Сдаётся квартира {id}</h4></div>"
                    + "<div data-testid='ad_description'><div>Долгосрочная аренда, Юнусабад</div></div>"
                    + "<nav data-testid='breadcrumbs'><a href='/nedvizhimost/kvartiry/arenda-dolgosrochnaya/tashkent/'>Квартиры</a></nav>"
                    + $"<div data-testid='ad-parameters-container'><p>Количество комнат: {(id == "Ab12" ? 2 : 3)}</p></div>"
                    + "<div data-testid='ad-price-container'><h3>5 000 000 сум</h3></div>";
            }
            byte[] bytes = Encoding.UTF8.GetBytes(html);
            return Task.FromResult(new FetchResponse
            {
                Outcome = FetchOutcome.Ok,
                Usage = new() { Requests = 1, Bytes = bytes.Length },
                Document = new()
                {
                    RequestUrl = request.Uri, FinalUrl = request.Uri, StatusCode = 200,
                    ContentType = "text/html", Body = bytes, FetchedAtUtc = DateTimeOffset.UtcNow,
                },
            });
        }
    }
}
