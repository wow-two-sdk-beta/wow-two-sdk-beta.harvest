using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using HarvestSources.Api.Configurations;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using WoW.Two.Sdk.Beta.Be.Testing;

namespace HarvestSources.Tests.E2E;

public sealed class SourceHostTests
{
    [Fact]
    public async Task Public_catalog_uses_generic_site_support_and_reserves_pool_registration_for_operators()
    {
        const string operatorKey = "hv_PublicCatalogTestOperator0000000";
        string database = await SourcePostgres.NewDatabaseAsync();
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(Program).Assembly.GetName().Name,
            EnvironmentName = "Development",
        });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DatabaseSettings:ConnectionString"] = database,
            ["Harvest:Operator:ApiKey"] = operatorKey,
            ["Harvest:Worker:Enabled"] = "false",
            ["HarvestSources:Audience"] = "public",
            ["AllowedHosts"] = "localhost",
            ["Logging:LogLevel:Default"] = "Warning",
        });
        builder.WebHost.UseTestServer();
        await using var host = builder.Configure().Build();
        await host.ConfigureAsync();
        await host.StartAsync();
        using HttpClient client = host.GetTestClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", operatorKey);
        (await client.GetAsync("/api/ocharo/sources/presets")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        string container = (await Data(await client.PostAsJsonAsync("/api/containers", new { kind = "pool" })))
            .GetProperty("id").GetString()!;
        var registration = new
        {
            containerId = container, family = "site", handle = "www.afisha.uz", coverage = "filtered",
            completeness = "window", posture = "ownFetch", checkPolicy = "web-direct", robotsPolicy = "obey",
        };
        string afisha = (await Data(await client.PostAsJsonAsync("/api/sources", registration))).GetProperty("id").GetString()!;
        JsonElement capabilities = await Data(await client.GetAsync($"/api/sources/{afisha}/capabilities"));
        capabilities.GetProperty("adapterKey").GetString().Should().Be("generic-site");
        capabilities.GetProperty("configuration").GetProperty("scopeSchema").TryGetProperty("sections", out _)
            .Should().BeFalse();

        string listing = (await Data(await client.PostAsJsonAsync("/api/sources",
            registration with { family = "listing", handle = "olx.uz" }))).GetProperty("id").GetString()!;
        (await Data(await client.GetAsync($"/api/sources/{listing}/capabilities"))).GetProperty("configuration")
            .GetProperty("readiness").GetString().Should().Be("unavailable");
        string tenant = (await Data(await client.PostAsJsonAsync("/api/tenants", new { name = "Catalog reader", slug = "catalog-reader" })))
            .GetProperty("id").GetString()!;
        JsonElement issued = await Data(await client.PostAsJsonAsync($"/api/tenants/{tenant}/api-keys",
            new { name = "Reader", scopes = new[] { "subscriptions:manage", "events:read" } }));
        using HttpClient reader = host.GetTestClient();
        reader.DefaultRequestHeaders.Add("X-Api-Key", issued.GetProperty("secret").GetString());
        (await reader.GetAsync($"/api/sources?containerId={container}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await reader.PostAsJsonAsync("/api/sources", registration)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Private_host_reuses_public_api_and_requires_operator_for_source_setup()
    {
        const string operatorKey = "hv_SourceHostTestOperator0000000000";
        string database = await SourcePostgres.NewDatabaseAsync();
        await using var host = new WebApiTestHost<Program>
        {
            ConfigureConfigurationHook = configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseSettings:ConnectionString"] = database,
                ["Harvest:Operator:ApiKey"] = operatorKey,
                ["Harvest:Worker:Enabled"] = "false",
                ["AllowedHosts"] = "localhost",
                ["Logging:LogLevel:Default"] = "Warning",
                ["HarvestInstagram:AccessToken"] = "synthetic-provider-token-must-stay-private",
            }),
        };
        using HttpClient anonymous = host.CreateClient();
        (await anonymous.GetAsync("/api/system/status")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await anonymous.GetAsync("/api/ocharo/sources/presets")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using HttpClient client = host.CreateClient();
        client.DefaultRequestHeaders.Add("X-Api-Key", operatorKey);
        JsonElement presets = await Data(await client.GetAsync("/api/ocharo/sources/presets"));
        presets.GetArrayLength().Should().Be(5);
        JsonElement setup = await Data(await client.PostAsJsonAsync("/api/ocharo/sources/setup", new { enableSchedules = false }));
        setup.GetProperty("sources").GetArrayLength().Should().Be(5);
        string tenant = setup.GetProperty("tenantId").GetString()!;
        string container = setup.GetProperty("containerId").GetString()!;
        JsonElement repeated = await Data(await client.PostAsJsonAsync("/api/ocharo/sources/setup", new { enableSchedules = false }));
        repeated.GetProperty("containerId").GetString().Should().Be(container);
        var firstSubscriptions = setup.GetProperty("sources").EnumerateArray().Select(item => item.GetProperty("subscriptionId").GetString()).ToArray();
        repeated.GetProperty("sources").EnumerateArray().Select(item => item.GetProperty("subscriptionId").GetString()).Should().Equal(firstSubscriptions);
        JsonElement sources = await Data(await client.GetAsync("/api/sources?containerId=" + container));
        sources.GetArrayLength().Should().Be(4);
        string afisha = sources.EnumerateArray().Single(item => item.GetProperty("handle").GetString()!.Contains("afisha")).GetProperty("id").GetString()!;
        JsonElement capabilities = await Data(await client.GetAsync($"/api/sources/{afisha}/capabilities"));
        capabilities.GetProperty("adapterKey").GetString().Should().Be("afisha-uz");
        capabilities.GetProperty("configuration").GetProperty("displayName").GetString().Should().Be("Afisha Uzbekistan");
        capabilities.GetProperty("workKinds").EnumerateArray().Select(item => item.GetString()).Should().NotContain("metrics");
        JsonElement withInstagram = await Data(await client.PostAsJsonAsync("/api/ocharo/sources/setup", new { enableSchedules = true, includeInstagram = true }));
        withInstagram.GetProperty("sources").GetArrayLength().Should().Be(8);
        JsonElement pipelines = await Data(await client.GetAsync("/api/pipelines?containerId=" + container));
        var instagram = pipelines.EnumerateArray().Where(item => item.GetProperty("name").GetString()!.Contains("Instagram")).ToArray();
        instagram.Should().HaveCount(3);
        string instagramSource = instagram[0].GetProperty("sourceId").GetString()!;
        JsonElement disabled = await Data(await client.GetAsync($"/api/sources/{instagramSource}/capabilities"));
        disabled.GetProperty("configuration").GetProperty("readiness").GetString().Should().Be("configurationRequired");
        disabled.GetRawText().Should().NotContain("synthetic-provider-token-must-stay-private");
        instagram.Should().OnlyContain(item => item.GetProperty("schedules").EnumerateArray().All(schedule => !schedule.GetProperty("enabled").GetBoolean()));
        JsonElement issued = await Data(await client.PostAsJsonAsync("/api/tenants/" + tenant + "/api-keys",
            new { name = "Ocharo reader", scopes = new[] { "events:read", "pipelines:read", "posts:read" } }));
        using HttpClient reader = host.CreateClient();
        reader.DefaultRequestHeaders.Add("X-Api-Key", issued.GetProperty("secret").GetString());
        (await reader.PostAsJsonAsync("/api/ocharo/sources/setup", new { enableSchedules = true })).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await reader.GetAsync("/api/sources?containerId=" + container)).StatusCode.Should().Be(HttpStatusCode.OK);
        (await reader.GetAsync($"/api/sources/{afisha}/capabilities")).StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task Private_host_exposes_the_api_without_serving_a_console()
    {
        const string operatorKey = "hv_SourceConsoleOperator00000000000";
        string database = await SourcePostgres.NewDatabaseAsync();
        await using var host = new WebApiTestHost<Program>
        {
            ConfigureConfigurationHook = configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseSettings:ConnectionString"] = database,
                ["Harvest:Operator:ApiKey"] = operatorKey,
                ["Harvest:Worker:Enabled"] = "false",
                ["AllowedHosts"] = "localhost",
                ["Logging:LogLevel:Default"] = "Warning",
            }),
        };
        using HttpClient client = host.CreateClient();
        using HttpResponseMessage page = await client.GetAsync("/");
        page.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/harvest-console.json")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/api/pipelines")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        client.DefaultRequestHeaders.Add("X-Api-Key", operatorKey);
        (await client.GetAsync("/api/does-not-exist")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/api/greetings")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/api/system/status")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync("/health")).StatusCode.Should().Be(HttpStatusCode.OK);
        client.DefaultRequestHeaders.Host = "untrusted.example.test";
        (await client.GetAsync("/api/system/status")).StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    private static async Task<JsonElement> Data(HttpResponseMessage response)
    {
        using (response)
        {
            string body = await response.Content.ReadAsStringAsync();
            response.IsSuccessStatusCode.Should().BeTrue(body);
            using JsonDocument json = JsonDocument.Parse(body);
            return json.RootElement.GetProperty("data").Clone();
        }
    }
}
