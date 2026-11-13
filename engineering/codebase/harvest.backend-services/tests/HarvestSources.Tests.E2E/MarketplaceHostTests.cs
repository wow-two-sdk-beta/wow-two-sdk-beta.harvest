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

namespace HarvestSources.Tests.E2E;

/// <summary>Provisions a marketplace profile over HTTP and pulls its listings with the consumer's own key, in the shape
/// Haven Supply's <c>HarvestOlxClient</c> parses.</summary>
public sealed class MarketplaceHostTests
{
    private const string OperatorKey = "hv_MarketplaceHostTestOperator00000";

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

    private static async Task<WebApplication> StartAsync(OlxFixtureFetcher fetcher)
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
    private static async Task RunAsync(HttpClient client, IServiceProvider services, string pipelineId)
    {
        var run = await Data(await client.PostAsJsonAsync("/api/runs", new { pipelineId }));
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
                return;
            }
            await Task.Delay(TimeSpan.FromMilliseconds(250));
        }
        throw new TimeoutException("The OLX fixture capture did not finish.");
    }

    private static async Task<JsonElement> PullAsync(HttpClient client, string url)
    {
        // A pull reads below the oldest open transaction, so a concurrent one can briefly hide a committed event.
        for (var attempt = 1; ; attempt++)
        {
            var page = await Data(await client.GetAsync(url));
            if (page.GetProperty("data").GetArrayLength() >= 2 || attempt == 20)
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
