using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using HarvestSources.Api.Configurations;
using HarvestSources.Api.Sources;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;

namespace HarvestSources.Tests.E2E;

/// <summary>The instance's source catalog on the source host: <c>GET api/sources/catalog</c> for the operator, <c>PUT
/// api/sources/catalog/{key}</c> for <c>ManageSources</c>, what the Sources page then lists, and the <c>sources catalog</c> verb.</summary>
public sealed class SourceCatalogTests
{
    private const string OperatorKey = "hv_SourceCatalogOperator00000000000";

    [Fact]
    public async Task Catalog_read_serves_the_operator_only()
    {
        await using var host = await StartAsync();
        using var client = Client(host, OperatorKey);
        using var tenant = Client(host, await TenantKeyAsync(client));
        using var anonymous = host.GetTestClient();

        var response = await client.GetAsync("/api/sources/catalog");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
        var entries = (await DataAsync(response)).EnumerateArray().ToArray();
        entries.Should().HaveCount(31, "27 Ocharo publishers and 4 marketplace sites");
        entries.Should().OnlyContain(entry => entry.GetProperty("state").GetString() == "new", "a fresh instance starts with every entry new");
        var afisha = entries.Single(entry => entry.GetProperty("key").GetString() == "ocharo.afisha");
        afisha.GetProperty("catalog").GetString().Should().Be("ocharo");
        afisha.GetProperty("name").GetString().Should().Be("Afisha Uzbekistan");
        afisha.GetProperty("kind").GetString().Should().Be("events");
        afisha.GetProperty("channels").GetInt32().Should().Be(2);
        afisha.GetProperty("hosts")[0].GetProperty("handle").GetString().Should().Be("www.afisha.uz");
        afisha.GetProperty("sources").GetArrayLength().Should().Be(0);
        (await tenant.GetAsync("/api/sources/catalog")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await anonymous.GetAsync("/api/sources/catalog")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task Catalog_switch_serves_the_operator_answers_not_found_and_drives_the_sources_page()
    {
        await using var host = await StartAsync();
        using var client = Client(host, OperatorKey);
        using var tenant = Client(host, await TenantKeyAsync(client));

        (await tenant.PutAsJsonAsync("/api/sources/catalog/ocharo.afisha", new { turnedOn = true })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.PutAsJsonAsync("/api/sources/catalog/ocharo.nothing", new { turnedOn = true })).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/api/sources/catalog")).StatusCode.Should().Be(HttpStatusCode.OK);

        var off = await client.PutAsJsonAsync("/api/sources/catalog/ocharo.anons", new { turnedOn = false });
        off.StatusCode.Should().Be(HttpStatusCode.OK);
        var anons = await DataAsync(off);
        (anons.GetProperty("state").GetString(), anons.GetProperty("changedBy").GetString()).Should().Be(("off", "operator-key"));
        anons.GetProperty("sources").GetArrayLength().Should().Be(0, "a new entry turned off is not set up");

        var on = await client.PutAsJsonAsync("/api/sources/catalog/ocharo.afisha", new { turnedOn = true });
        on.StatusCode.Should().Be(HttpStatusCode.OK);
        var afisha = await DataAsync(on);
        afisha.GetProperty("state").GetString().Should().Be("on");
        var source = afisha.GetProperty("sources").EnumerateArray().Should().ContainSingle().Subject;
        string sourceId = source.GetProperty("sourceId").GetString()!, containerId = source.GetProperty("containerId").GetString()!;
        (await DataAsync(await client.GetAsync($"/api/sources/overview?containerId={containerId}"))).EnumerateArray()
            .Should().ContainSingle(item => item.GetProperty("sourceId").GetString() == sourceId && item.GetProperty("catalogKey").GetString() == "ocharo.afisha");

        var disable = await client.PutAsJsonAsync("/api/sources/catalog/ocharo.afisha", new { turnedOn = false });
        (await DataAsync(disable)).GetProperty("state").GetString().Should().Be("off");

        (await DataAsync(await client.GetAsync($"/api/sources/overview?containerId={containerId}"))).GetArrayLength().Should().Be(0, "the Sources page lists turned-on sources only");
        var byId = await DataAsync(await client.GetAsync($"/api/sources/{sourceId}"));
        (byId.GetProperty("turnedOn").GetBoolean(), byId.GetProperty("catalogKey").GetString()).Should().Be((false, "ocharo.afisha"));
        (await DataAsync(await client.PostAsJsonAsync("/api/ocharo/sources/setup", new { }))).GetProperty("skipped").EnumerateArray()
            .Select(item => item.GetString()).Should().Equal("ocharo.afisha", "ocharo.anons");
        var listed = (await DataAsync(await client.GetAsync("/api/sources/catalog"))).EnumerateArray()
            .Single(entry => entry.GetProperty("key").GetString() == "ocharo.afisha");
        listed.GetProperty("state").GetString().Should().Be("off");
        listed.GetProperty("sources").GetArrayLength().Should().Be(1, "the entry still names its source");
        (await DataAsync(await client.PutAsJsonAsync("/api/sources/catalog/ocharo.afisha", new { turnedOn = true }))).GetProperty("state").GetString().Should().Be("on");
    }

    [Fact]
    public async Task Catalog_verb_lists_filters_and_switches_one_json_line_each()
    {
        await using var host = await StartAsync();

        var list = await VerbAsync(host.Services, "catalog");
        var off = await VerbAsync(host.Services, "catalog", "off", "ocharo.anons");
        var offOnly = await VerbAsync(host.Services, "catalog", "--state", "off");
        var on = await VerbAsync(host.Services, "catalog", "on", "ocharo.afisha");
        var onOnly = await VerbAsync(host.Services, "catalog", "--state", "on");
        var unknown = await VerbAsync(host.Services, "catalog", "on", "ocharo.nothing");
        var usage = await VerbAsync(host.Services, "catalog", "--state", "sometimes");

        list.Exit.Should().Be(0, list.Error);
        Lines(list.Output).Should().ContainSingle();
        Parse(list.Output).GetProperty("entries").GetArrayLength().Should().Be(31);
        off.Exit.Should().Be(0, off.Error);
        var anons = Parse(off.Output).GetProperty("entry");
        (anons.GetProperty("key").GetString(), anons.GetProperty("state").GetString(), anons.GetProperty("changedBy").GetString()).Should().Be(("ocharo.anons", "off", "operator-key"));
        Parse(offOnly.Output).GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("key").GetString()).Should().Equal("ocharo.anons");
        on.Exit.Should().Be(0, on.Error);
        var afisha = Parse(on.Output).GetProperty("entry");
        afisha.GetProperty("state").GetString().Should().Be("on");
        afisha.GetProperty("sources").GetArrayLength().Should().Be(1, "a new entry turned on is set up");
        Parse(onOnly.Output).GetProperty("entries").EnumerateArray().Select(entry => entry.GetProperty("key").GetString()).Should().Equal("ocharo.afisha");
        unknown.Exit.Should().Be(1);
        unknown.Error.Should().Contain("No catalog entry has the key 'ocharo.nothing'");
        unknown.Output.Should().BeEmpty();
        usage.Exit.Should().Be(2);
        usage.Error.Should().Contain("Usage: sources processing").And.Contain("sources catalog");
    }

    private static string[] Lines(string text) => text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static JsonElement Parse(string line)
    {
        using var document = JsonDocument.Parse(Lines(line).Last());
        return document.RootElement.Clone();
    }

    private static async Task<WebApplication> StartAsync()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = "Development",
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DB_CONNECTION"] = await SourcePostgres.NewDatabaseAsync(),
            ["Harvest:Operator:ApiKey"] = OperatorKey,
            ["Harvest:Worker:Enabled"] = "false",
            ["AllowedHosts"] = "localhost",
            ["Logging:LogLevel:Default"] = "Warning",
        });
        builder.WebHost.UseTestServer();
        builder.Configure();
        var host = builder.Build();
        await host.ConfigureAsync();
        await host.StartAsync();
        return host;
    }

    private static HttpClient Client(WebApplication host, string key)
    {
        var client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", key);
        return client;
    }

    private static async Task<string> TenantKeyAsync(HttpClient client)
    {
        string tenant = (await DataAsync(await client.PostAsJsonAsync("/api/tenants", new { name = "Reader", slug = "reader" })))
            .GetProperty("id").GetString()!;
        return (await DataAsync(await client.PostAsJsonAsync($"/api/tenants/{tenant}/api-keys",
            new { name = "Reader", scopes = new[] { "posts:read", "pipelines:run" } }))).GetProperty("secret").GetString()!;
    }

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        string body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(body);
        using var document = JsonDocument.Parse(body);
        return document.RootElement.GetProperty("data").Clone();
    }

    private static async Task<(int Exit, string Output, string Error)> VerbAsync(IServiceProvider services, params string[] args)
    {
        using var output = new StringWriter();
        using var error = new StringWriter();
        int exit = await SourcesVerb.RunAsync(services, args, output, error, CancellationToken.None);
        return (exit, output.ToString(), error.ToString());
    }
}
