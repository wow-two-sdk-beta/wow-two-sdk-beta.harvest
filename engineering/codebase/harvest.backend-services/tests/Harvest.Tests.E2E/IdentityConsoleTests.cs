using System.Net;
using System.Security.Claims;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Harvest.Api.Configurations;
using Harvest.Api.Console;
using Harvest.Tests.Shared;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Protocols;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Identity.Bff;

namespace Harvest.Tests.E2E;

/// <summary>Exercises real BFF cookies, PostgreSQL sessions, CSRF and organization-scoped Harvest access.</summary>
public sealed class IdentityConsoleTests : IAsyncLifetime
{
    private readonly Guid _organization = Guid.NewGuid();
    private readonly string _keys = Path.Combine(Path.GetTempPath(), "harvest-identity-" + Guid.NewGuid().ToString("N"));
    private readonly string _operator = Guid.NewGuid().ToString("D");
    private WebApplication _host = null!;
    private Guid _tenant;
    private Guid _container;
    private Guid _foreignTenant;
    private Guid _foreignContainer;

    public async Task InitializeAsync()
    {
        var connection = (await PostgresServer.SharedAsync()).NewDatabase("identity_console");
        var organizationKey = _organization.ToString("D");
        var configuredTenant = Guid.NewGuid();
        var settings = new Dictionary<string, string?>
        {
            ["DatabaseSettings:ConnectionString"] = connection,
            [HostConfiguration.OperatorApiKeySetting] = HarvestApiFixture.OperatorKey,
            ["Harvest:Console:IsIdentityEnabled"] = "true",
            ["Harvest:Console:OperatorSubjectIds:0"] = _operator,
            [$"Harvest:Console:OrganizationTenants:{organizationKey.ToUpperInvariant()}"] = configuredTenant.ToString("D"),
            ["Identity:Client:Authority"] = "https://identity.example.test/",
            ["Identity:Client:ClientId"] = "harvest-tests",
            ["Identity:Client:ClientSecret"] = "test-only-client-secret",
            ["Identity:Client:CookieName"] = "__Host-harvest-tests",
            ["DataProtection:KeysPath"] = _keys,
            ["Harvest:Worker:Enabled"] = "false",
            ["Logging:LogLevel:Default"] = "Warning",
        };
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions { EnvironmentName = "Development" });
        builder.Configuration.AddInMemoryCollection(settings);
        builder.WebHost.UseTestServer();
        builder.Configure();
        builder.Services.PostConfigure<OpenIdConnectOptions>(BffAuthenticationSchemeConstants.OpenIdConnect, options =>
        {
            options.Configuration = new OpenIdConnectConfiguration
            {
                Issuer = "https://identity.example.test/",
                AuthorizationEndpoint = "https://identity.example.test/connect/authorize",
                TokenEndpoint = "https://identity.example.test/connect/token",
                EndSessionEndpoint = "https://identity.example.test/connect/endsession",
            };
            options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(options.Configuration);
        });
        _host = builder.Build();
        await _host.ConfigureAsync();
        await _host.StartAsync();
        using var scope = _host.Services.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        _tenant = (await registry.RegisterTenantAsync(new() { Slug = "own", Name = "Own" })).ValueOrThrow().Id;
        _foreignTenant = (await registry.RegisterTenantAsync(new() { Slug = "other", Name = "Other" })).ValueOrThrow().Id;
        _container = (await registry.RegisterContainerAsync(new() { Kind = ContainerKind.Private, TenantId = _tenant })).ValueOrThrow().Id;
        _foreignContainer = (await registry.RegisterContainerAsync(new() { Kind = ContainerKind.Private, TenantId = _foreignTenant })).ValueOrThrow().Id;
        var bindings = _host.Services.GetRequiredService<IOptions<HarvestConsoleOptions>>().Value.OrganizationTenants;
        bindings.Should().Contain(organizationKey, configuredTenant);
        bindings[organizationKey] = _tenant;
    }

    public async Task DisposeAsync() => await _host.DisposeAsync();

    [Fact]
    public async Task AnonymousConsole_ShouldExposeSignIn_WithoutGrantingRegistryAccess()
    {
        using var client = Client();
        var access = await DataAsync(await client.GetAsync("/api/console/access"));
        access.GetProperty("identityEnabled").GetBoolean().Should().BeTrue();
        access.GetProperty("hasAccess").GetBoolean().Should().BeFalse();
        (await DataAsync(await client.GetAsync("/api/identity/session"))).GetProperty("isAuthenticated").GetBoolean().Should().BeFalse();
        (await client.GetAsync("/api/tenants")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        using var signIn = await client.GetAsync("/api/identity/sign-in?returnUrl=%2F");
        signIn.StatusCode.Should().Be(HttpStatusCode.Redirect, await signIn.Content.ReadAsStringAsync());
        signIn.Headers.Location!.AbsoluteUri.Should().StartWith("https://identity.example.test/connect/authorize?")
            .And.Contain("code_challenge=").And.Contain("code_challenge_method=S256");
    }

    [Fact]
    public async Task BoundSession_ShouldReadOnlyItsOwnTenantAndContainers()
    {
        using var client = await SessionAsync(_organization, "pipelines:read", "posts:read", "events:read");
        var tenants = await DataAsync(await client.GetAsync("/api/tenants"));
        tenants.EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).Should().Equal(_tenant);
        var containers = await DataAsync(await client.GetAsync("/api/containers"));
        containers.EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).Should().Contain(_container).And.NotContain(_foreignContainer);
        (await client.GetAsync($"/api/tenants/{_foreignTenant}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync($"/api/pipelines?containerId={_foreignContainer}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
        (await client.GetAsync("/api/subscriptions")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/api/subscriptions?tenantId={_foreignTenant}")).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task SessionWrites_ShouldRequireCsrf_AndRefuseAnotherTenantsContainer()
    {
        using var client = await SessionAsync(_organization, "pipelines:read", "sources:manage");
        using var withoutCsrf = await PostAsync(client, "/api/sources", Source(_container));
        withoutCsrf.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        client.DefaultRequestHeaders.Add("X-Csrf-Token", await CsrfAsync(client));
        (await PostAsync(client, "/api/sources", Source(_container))).StatusCode.Should().Be(HttpStatusCode.Created);
        (await PostAsync(client, "/api/sources", Source(_foreignContainer))).StatusCode.Should().Be(HttpStatusCode.NotFound);
    }

    [Fact]
    public async Task ReadOnlySession_ShouldBeForbiddenWrites_EvenWithValidCsrf()
    {
        using var client = await SessionAsync(_organization, "pipelines:read", "posts:read");
        client.DefaultRequestHeaders.Add("X-Csrf-Token", await CsrfAsync(client));
        (await PostAsync(client, "/api/sources", Source(_container))).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task OrganizationOwner_ShouldNeverBecomeARegistryOperator()
    {
        using var client = await SessionAsync(_organization, [.. HarvestScopeConstants.All]);
        client.DefaultRequestHeaders.Add("X-Csrf-Token", await CsrfAsync(client));
        (await PostAsync(client, "/api/tenants", """{"slug":"unauthorized","name":"Unauthorized"}""")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await client.GetAsync($"/api/tenants/{_tenant}/api-keys")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PostAsync(client, "/api/containers", """{"kind":"pool"}""")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var foreign = JsonSerializer.Serialize(new { kind = "private", tenantId = _foreignTenant });
        (await PostAsync(client, "/api/containers", foreign)).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PostAsync(client, "/api/posts/captures", "{}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        (await PostAsync(client, "/api/post-merges", "{}")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task UnboundOrganization_ShouldHaveNoHarvestAccess()
    {
        using var client = await SessionAsync(Guid.NewGuid(), "pipelines:read", "sources:manage");
        (await client.GetAsync("/api/containers")).StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var access = await DataAsync(await client.GetAsync("/api/console/access"));
        access.GetProperty("hasAccess").GetBoolean().Should().BeFalse();
    }

    [Fact]
    public async Task InvalidServiceKey_ShouldNotFallBackToAnAuthenticatedCookie()
    {
        using var client = await SessionAsync(_organization, "pipelines:read");
        client.DefaultRequestHeaders.Add("X-Api-Key", "hv_invalid");
        (await client.GetAsync("/api/containers")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task ServiceOperator_ShouldStillWorkWithoutABrowserCookieOrCsrf()
    {
        using var client = Client();
        client.DefaultRequestHeaders.Add("X-Api-Key", HarvestApiFixture.OperatorKey);
        (await PostAsync(client, "/api/tenants", """{"slug":"service-created","name":"Service"}""")).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    [Fact]
    public async Task ExplicitPlatformOperator_ShouldManageTenantsAndSeeTheirContainers()
    {
        using var client = await SessionAsync(Guid.NewGuid(), [], _operator);
        var access = await DataAsync(await client.GetAsync("/api/console/access"));
        access.GetProperty("isOperator").GetBoolean().Should().BeTrue();
        var containers = await DataAsync(await client.GetAsync("/api/containers"));
        containers.EnumerateArray().Select(row => row.GetProperty("id").GetGuid()).Should().Contain(_container).And.Contain(_foreignContainer);
        client.DefaultRequestHeaders.Add("X-Csrf-Token", await CsrfAsync(client));
        (await PostAsync(client, "/api/tenants", """{"slug":"console-created","name":"Console"}""")).StatusCode.Should().Be(HttpStatusCode.Created);
    }

    private HttpClient Client()
    {
        var client = _host.GetTestClient();
        client.BaseAddress = new Uri("https://localhost");
        return client;
    }

    // The test starts at the validated-principal boundary. SignInAsync uses the actual BFF cookie events,
    // PostgreSQL session repository, protection and CSRF; no production route or authentication bypass is added.
    private Task<HttpClient> SessionAsync(Guid organization, params string[] scopes) =>
        SessionAsync(organization, scopes, Guid.NewGuid().ToString("D"));

    private async Task<HttpClient> SessionAsync(Guid organization, string[] scopes, string subject)
    {
        using var services = _host.Services.CreateScope();
        var context = new DefaultHttpContext { RequestServices = services.ServiceProvider };
        context.Request.Scheme = "https";
        context.Request.Host = new HostString("localhost");
        var claims = new List<Claim>
        {
            new("sub", subject), new("sid", Guid.NewGuid().ToString()), new("name", "Reviewer"),
            new("org_id", organization.ToString("D")), new("org_role", organization.ToString("D") + ":owner"),
        };
        claims.AddRange(scopes.Select(scope => new Claim("org_permission", organization.ToString("D") + ":harvest:" + scope)));
        await context.SignInAsync(BffAuthenticationSchemeConstants.Cookie,
            new ClaimsPrincipal(new ClaimsIdentity(claims, BffAuthenticationSchemeConstants.Cookie)), new AuthenticationProperties());
        var client = Client();
        client.DefaultRequestHeaders.Add("Cookie", context.Response.Headers.SetCookie.Single()!.Split(';')[0]);
        return client;
    }

    private static async Task<string> CsrfAsync(HttpClient client) =>
        (await DataAsync(await client.GetAsync("/api/identity/session"))).GetProperty("csrfToken").GetString()!;

    private static string Source(Guid container) => $$"""
        {"containerId":"{{container}}","family":"page","handle":"source-{{Guid.NewGuid():N}}.example",
         "coverage":"single","completeness":"none","posture":"ownFetch"}
        """;

    private static Task<HttpResponseMessage> PostAsync(HttpClient client, string path, string body) =>
        client.PostAsync(path, new StringContent(body, Encoding.UTF8, "application/json"));

    private static async Task<JsonElement> DataAsync(HttpResponseMessage response)
    {
        using (response)
        {
            var body = await response.Content.ReadAsStringAsync();
            response.IsSuccessStatusCode.Should().BeTrue(body);
            return JsonDocument.Parse(body).RootElement.GetProperty("data").Clone();
        }
    }
}
