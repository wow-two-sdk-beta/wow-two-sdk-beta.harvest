using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using HarvestSources.Api.Configurations;
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

public sealed class OcharoRegistryHostTests
{
    [Fact]
    public async Task Operator_provisions_a_group_and_pulls_reviewable_calendar_revisions()
    {
        const string operatorKey = "hv_OcharoRegistryTestOperator000000";
        var fetcher = new CalendarFixtureFetcher();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = "Development",
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DatabaseSettings:ConnectionString"] = await SourcePostgres.NewDatabaseAsync(),
            ["Harvest:Operator:ApiKey"] = operatorKey,
            ["Harvest:Worker:Enabled"] = "false",
            ["AllowedHosts"] = "localhost",
            ["Logging:LogLevel:Default"] = "Warning",
        });
        builder.WebHost.UseTestServer();
        builder.Configure();
        builder.Services.RemoveAll<IPageFetcher>();
        builder.Services.AddSingleton<IPageFetcher>(fetcher);

        await using var host = builder.Build();
        await host.ConfigureAsync();
        await host.StartAsync();

        using HttpClient client = host.GetTestClient();
        (await client.GetAsync("/api/ocharo/sources/registry")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Add("X-Api-Key", operatorKey);
        var registry = await Data(await client.GetAsync("/api/ocharo/sources/registry"));
        registry.GetProperty("format").GetString().Should().Be("ocharo.event-sources.v1");
        registry.GetProperty("groups").GetArrayLength().Should().Be(100);
        (await Data(await client.GetAsync("/api/tenants"))).GetArrayLength().Should().Be(0);

        var selection = new { groupIds = new[] { "caex" } };
        var route = (await Data(await client.PostAsJsonAsync("/api/ocharo/sources/setup", selection)))
            .GetProperty("sources").EnumerateArray().Single();
        route.GetProperty("groupId").GetString().Should().Be("caex");
        string pipelineId = route.GetProperty("pipelineId").GetString()!;
        string subscriptionId = route.GetProperty("subscriptionId").GetString()!;
        var repeated = (await Data(await client.PostAsJsonAsync("/api/ocharo/sources/setup", selection)))
            .GetProperty("sources").EnumerateArray().Single();
        repeated.GetProperty("subscriptionId").GetString().Should().Be(subscriptionId);
        fetcher.Calls.Should().Be(0);

        await RunAsync(client, host.Services, pipelineId);
        string pullUrl = $"/api/subscriptions/{subscriptionId}/events";
        var first = await PullAsync(client, pullUrl);
        var delivery = first.GetProperty("data").EnumerateArray().Should().ContainSingle().Subject;
        var payload = delivery.GetProperty("data");
        delivery.GetProperty("context").GetProperty("sourceGroup").GetString().Should().Be("caex");
        delivery.GetProperty("context").GetProperty("sourceChannel").GetString().Should().Be("calendar");
        delivery.GetProperty("context").TryGetProperty("review", out _).Should().BeFalse();
        payload.GetProperty("schema").GetString().Should().Be("wow2.event.v1");
        payload.GetProperty("title").GetString().Should().Be("TITF 2026");
        payload.GetProperty("starts").GetProperty("value").GetString().Should().StartWith("2026-11-25");
        payload.GetProperty("schemaOrg").GetProperty("caex").GetProperty("calendarUrl").GetString()
            .Should().Be("https://caexuzbekistan.com/events-calendar/");

        fetcher.Dates = "November 26–28, 2026";
        await RunAsync(client, host.Services, pipelineId);
        var next = await PullAsync(client, $"{pullUrl}?cursor={Uri.EscapeDataString(first.GetProperty("next").GetString()!)}");
        var correction = next.GetProperty("data").EnumerateArray().Should().ContainSingle().Subject;
        correction.GetProperty("postId").GetGuid().Should().Be(delivery.GetProperty("postId").GetGuid());
        correction.GetProperty("revision").GetInt32().Should().BeGreaterThan(delivery.GetProperty("revision").GetInt32());
        correction.GetProperty("data").GetProperty("starts").GetProperty("value").GetString().Should().StartWith("2026-11-26");
        fetcher.Calls.Should().Be(2);
    }

    private static async Task RunAsync(HttpClient client, IServiceProvider services, string pipelineId)
    {
        var run = await Data(await client.PostAsJsonAsync("/api/runs", new { pipelineId }));
        await using var scope = services.CreateAsyncScope();
        var engine = scope.ServiceProvider.GetRequiredService<HarvestEngine>();
        (await engine.ProcessNextAsync()).Should().BeTrue();
        (await engine.ProcessNextAsync()).Should().BeFalse();
        var completed = await Data(await client.GetAsync($"/api/runs/{run.GetProperty("id").GetString()}"));
        completed.GetProperty("status").GetString().Should().Be("completed");
    }

    private static async Task<JsonElement> PullAsync(HttpClient client, string url)
    {
        // A pull reads below the oldest open transaction, so a concurrent one can briefly hide a committed event.
        for (var attempt = 1; ; attempt++)
        {
            var page = await Data(await client.GetAsync(url));
            if (page.GetProperty("data").GetArrayLength() > 0 || attempt == 20)
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

    private sealed class CalendarFixtureFetcher : IPageFetcher
    {
        internal string Dates { get; set; } = "November 25–27, 2026";
        internal int Calls { get; private set; }

        public Task<FetchResponse> FetchAsync(FetchRequest request, CancellationToken cancellationToken = default)
        {
            Calls++;
            request.Uri.AbsoluteUri.Should().Be("https://caexuzbekistan.com/events-calendar/");
            request.RobotsPolicy.Should().Be(RobotsPolicy.Obey);
            request.DestinationPolicy!.Allows(new Uri("https://titf.uz/")).Should().BeFalse();
            byte[] bytes = Encoding.UTF8.GetBytes($"""
                <li><div class="caex-event-row">
                    <h3 class="wp-block-post-title"><a href="/2026/09/02/titf-2026/">TITF 2026</a></h3>
                    <p class="caex-event-row-date">{Dates}</p>
                    <p class="caex-event-row-organizer">Iteca Exhibitions</p>
                </div></li>
                <li><div class="caex-event-row">
                    <h3 class="wp-block-post-title"><a href="/2026/09/03/mebel-decor/">Mebel &amp; Décor</a></h3>
                    <p class="caex-event-row-date">May 20–22, 2027</p>
                </div></li>
                """);
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
