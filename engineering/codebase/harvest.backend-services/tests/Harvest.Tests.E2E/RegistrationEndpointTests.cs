using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;

namespace Harvest.Tests.E2E;

/// <summary>The registration API on the wire: the <c>{ data }</c> envelope, camelCase enums, problems, API key scopes and
/// tenant isolation.</summary>
[Collection(HarvestApiCollection.Name)]
public sealed class RegistrationEndpointTests(HarvestApiFixture fixture)
{
    [Fact]
    public async Task Api_ShouldAnswer401Problem_WhenNoKeyOrAMalformedKeyIsSent()
    {
        using var anonymous = fixture.CreateClient();
        using var malformed = fixture.CreateClient("hv_short");

        using var none = await anonymous.GetAsync("/api/tenants");
        using var bad = await malformed.GetAsync("/api/tenants");

        none.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        none.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        bad.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task PostTenant_ShouldAnswer201WithTheTenantInTheEnvelope_WhenTheOperatorRegistersIt()
    {
        using var client = fixture.CreateClient(HarvestApiFixture.OperatorKey);
        var slug = $"events-{Unique()}";

        using var response = await PostAsync(client, "/api/tenants", $$"""{"slug":" {{slug.ToUpperInvariant()}} ","name":"Events"}""");

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var tenant = await DataAsync(response);
        tenant.GetProperty("slug").GetString().Should().Be(slug);
        tenant.GetProperty("state").GetString().Should().Be("active");
    }

    [Fact]
    public async Task PostTenant_ShouldAnswer400Problem_WhenTheSlugIsMalformedOrTheNameMissing()
    {
        using var client = fixture.CreateClient(HarvestApiFixture.OperatorKey);

        using var malformed = await PostAsync(client, "/api/tenants", """{"slug":"Bad Slug","name":"Events"}""");
        using var missing = await PostAsync(client, "/api/tenants", """{"slug":"events-missing-name"}""");

        malformed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        malformed.Content.Headers.ContentType?.MediaType.Should().Be("application/problem+json");
        missing.StatusCode.Should().Be(HttpStatusCode.BadRequest);
    }

    [Fact]
    public async Task PostSource_ShouldReadAndWriteCamelCaseEnums_WhenTheOperatorRegistersIt()
    {
        using var client = fixture.CreateClient(HarvestApiFixture.OperatorKey);
        var tenant = await TenantAsync(client);
        var container = await ContainerAsync(client, tenant);

        using var response = await PostAsync(client, "/api/sources", $$"""
            {"containerId":"{{container}}","family":"structured","handle":"https://Voyage.uz/events","coverage":"whole",
             "completeness":"complete","posture":"ownFetch","robotsPolicy":"ignore",
             "capabilityPolicy": { "steps": { "detail": { "lane": "detail", "maxUsdPerMonth": 3 } } } }
            """);

        response.StatusCode.Should().Be(HttpStatusCode.Created);
        var source = await DataAsync(response);
        source.GetProperty("handle").GetString().Should().Be("voyage.uz");
        source.GetProperty("posture").GetString().Should().Be("ownFetch");
        source.GetProperty("permission").GetString().Should().Be("open");
        source.GetProperty("robotsPolicy").GetString().Should().Be("ignore");
        source.GetProperty("capabilityPolicy").GetProperty("steps").GetProperty("detail").GetProperty("lane").GetString().Should().Be("detail");
    }

    [Fact]
    public async Task TenantKey_ShouldBeForbiddenOperatorRoutes_WhenItHoldsTenantScopesOnly()
    {
        using var operatorClient = fixture.CreateClient(HarvestApiFixture.OperatorKey);
        var tenant = await TenantAsync(operatorClient);
        using var tenantClient = fixture.CreateClient(await KeyAsync(operatorClient, tenant, "pipelines:manage", "pipelines:read"));

        using var tenants = await tenantClient.GetAsync("/api/tenants");
        using var sources = await PostAsync(tenantClient, "/api/sources", "{}");

        tenants.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        sources.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task TenantKey_ShouldDeclareAndReadOnlyInItsOwnContainers_WhenOtherTenantsAndThePoolExist()
    {
        using var operatorClient = fixture.CreateClient(HarvestApiFixture.OperatorKey);
        var own = await TenantAsync(operatorClient);
        var other = await TenantAsync(operatorClient);
        var ownContainer = await ContainerAsync(operatorClient, own);
        var otherContainer = await ContainerAsync(operatorClient, other);
        var pool = await PoolAsync(operatorClient);
        var ownSource = await SourceAsync(operatorClient, ownContainer);
        var otherSource = await SourceAsync(operatorClient, otherContainer);
        var poolSource = await SourceAsync(operatorClient, pool);
        using var client = fixture.CreateClient(await KeyAsync(operatorClient, own, "pipelines:manage", "pipelines:read"));

        using var declared = await PostAsync(client, "/api/pipelines", Pipeline(ownContainer, ownSource));
        using var foreign = await PostAsync(client, "/api/pipelines", Pipeline(otherContainer, otherSource));
        using var pooled = await PostAsync(client, "/api/pipelines", Pipeline(pool, poolSource));
        using var foreignList = await client.GetAsync($"/api/pipelines?containerId={otherContainer}");
        using var containers = await client.GetAsync("/api/containers");

        declared.StatusCode.Should().Be(HttpStatusCode.Created);
        var pipeline = await DataAsync(declared);
        pipeline.GetProperty("state").GetString().Should().Be("active");
        pipeline.GetProperty("schedules")[0].GetProperty("every").GetString().Should().Be("PT1H");
        foreign.StatusCode.Should().Be(HttpStatusCode.NotFound);
        pooled.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        foreignList.StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await DataAsync(containers)).EnumerateArray().Select(container => container.GetProperty("id").GetString())
            .Should().Contain([ownContainer, pool]).And.NotContain(otherContainer);
        using var read = await client.GetAsync($"/api/pipelines/{pipeline.GetProperty("id").GetString()}");
        read.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task RevokedKey_ShouldAnswer401_WhenItIsPresentedAgain()
    {
        using var operatorClient = fixture.CreateClient(HarvestApiFixture.OperatorKey);
        var tenant = await TenantAsync(operatorClient);
        using var issued = await PostAsync(operatorClient, $"/api/tenants/{tenant}/api-keys", """{"name":"short-lived","scopes":["pipelines:read"]}""");
        var key = await DataAsync(issued);
        var secret = key.GetProperty("secret").GetString()!;
        var keyId = key.GetProperty("key").GetProperty("id").GetString();
        using var client = fixture.CreateClient(secret);
        var container = await ContainerAsync(operatorClient, tenant);

        using var before = await client.GetAsync($"/api/pipelines?containerId={container}");
        using var revoke = await operatorClient.DeleteAsync($"/api/tenants/{tenant}/api-keys/{keyId}");
        using var after = await client.GetAsync($"/api/pipelines?containerId={container}");

        before.StatusCode.Should().Be(HttpStatusCode.OK);
        revoke.StatusCode.Should().Be(HttpStatusCode.OK);
        (await DataAsync(revoke)).GetProperty("state").GetString().Should().Be("revoked");
        after.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    private static string Unique() => Guid.NewGuid().ToString("N")[..12];

    [Fact]
    public async Task Capabilities_ShouldUseTheInstalledAdapter_AndHideForeignSources()
    {
        using var admin = fixture.CreateClient(HarvestApiFixture.OperatorKey);
        var own = await TenantAsync(admin);
        var other = await TenantAsync(admin);
        var ownContainer = await ContainerAsync(admin, own);
        var otherContainer = await ContainerAsync(admin, other);
        var source = await SourceAsync(admin, ownContainer);
        var foreign = await SourceAsync(admin, otherContainer);
        using var reader = fixture.CreateClient(await KeyAsync(admin, own, "pipelines:read"));
        using var anonymous = fixture.CreateClient();

        (await anonymous.GetAsync($"/api/sources/{source}/capabilities")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await reader.GetAsync($"/api/sources/{foreign}/capabilities")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        using var response = await reader.GetAsync($"/api/sources/{source}/capabilities");
        JsonElement metadata = await DataAsync(response);
        metadata.GetProperty("sourceId").GetString().Should().Be(source);
        metadata.GetProperty("adapterKey").GetString().Should().NotBeNullOrWhiteSpace();
        metadata.GetProperty("workKinds").EnumerateArray().Select(kind => kind.GetString()).Should().Contain("capture");
        metadata.GetProperty("configuration").GetProperty("readiness").GetString().Should().Be("ready");
        metadata.GetProperty("configuration").GetProperty("scopeSchema").GetProperty("properties").GetProperty("url")
            .GetProperty("format").GetString().Should().Be("uri");
    }

    [Fact]
    public async Task Capabilities_ShouldReportUnavailable_WhenTheRuntimeHasNoImplementation()
    {
        using var admin = fixture.CreateClient(HarvestApiFixture.OperatorKey);
        var container = await ContainerAsync(admin, await TenantAsync(admin));
        using var registered = await PostAsync(admin, "/api/sources", $$"""
            {"containerId":"{{container}}","family":"instagram","handle":"instagram.com","coverage":"filtered",
             "completeness":"window","posture":"official"}
            """);
        var source = (await DataAsync(registered)).GetProperty("id").GetString();
        using var response = await admin.GetAsync($"/api/sources/{source}/capabilities");
        JsonElement metadata = await DataAsync(response);
        metadata.GetProperty("workKinds").GetArrayLength().Should().Be(0);
        metadata.GetProperty("configuration").GetProperty("readiness").GetString().Should().Be("unavailable");
    }

    private static string Pipeline(string container, string source) => $$"""
        {"containerId":"{{container}}","sourceId":"{{source}}",
         "schedules":[{"kind":"discover","mode":"interval","every":"PT1H"}]}
        """;

    private static async Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string json) =>
        await client.PostAsync(path, new StringContent(json, Encoding.UTF8, "application/json"));

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        var body = await response.Content.ReadAsStringAsync();
        response.IsSuccessStatusCode.Should().BeTrue(body);
        return JsonDocument.Parse(body).RootElement.GetProperty("data").Clone();
    }

    private static async Task<string> TenantAsync(HttpClient client)
    {
        using var response = await PostAsync(client, "/api/tenants", $$"""{"slug":"tenant-{{Unique()}}","name":"Tenant"}""");
        return (await DataAsync(response)).GetProperty("id").GetString()!;
    }

    private static async Task<string> ContainerAsync(HttpClient client, string tenant)
    {
        using var response = await PostAsync(client, "/api/containers", $$"""{"kind":"private","tenantId":"{{tenant}}"}""");
        return (await DataAsync(response)).GetProperty("id").GetString()!;
    }

    private static async Task<string> PoolAsync(HttpClient client)
    {
        using var created = await PostAsync(client, "/api/containers", """{"kind":"pool"}""");
        if (created.IsSuccessStatusCode)
            return (await DataAsync(created)).GetProperty("id").GetString()!;

        using var listed = await client.GetAsync("/api/containers");
        return (await DataAsync(listed)).EnumerateArray()
            .Single(container => container.GetProperty("kind").GetString() == "pool").GetProperty("id").GetString()!;
    }

    private static async Task<string> SourceAsync(HttpClient client, string container)
    {
        using var response = await PostAsync(client, "/api/sources", $$"""
            {"containerId":"{{container}}","family":"site","handle":"site-{{Unique()}}.example","coverage":"whole",
             "completeness":"complete","posture":"ownFetch"}
            """);
        return (await DataAsync(response)).GetProperty("id").GetString()!;
    }

    private static async Task<string> KeyAsync(HttpClient client, string tenant, params string[] scopes)
    {
        using var response = await PostAsync(client, $"/api/tenants/{tenant}/api-keys",
            JsonSerializer.Serialize(new { name = "program", scopes }));
        return (await DataAsync(response)).GetProperty("secret").GetString()!;
    }
}
