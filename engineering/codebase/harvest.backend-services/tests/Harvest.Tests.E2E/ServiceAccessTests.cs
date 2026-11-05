using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using AwesomeAssertions;
using Harvest.Tests.Shared;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.JsonWebTokens;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using WoW.Two.Sdk.Beta.Be.Testing;

namespace Harvest.Tests.E2E;

public sealed class ServiceAccessTests
{
    private const string Issuer = "https://identity.example.test/";
    private const string Audience = "https://harvest.example.test/";

    [Fact]
    public async Task Shared_api_accepts_bound_service_tokens_and_isolates_tenants()
    {
        string database = (await PostgresServer.SharedAsync()).NewDatabase("service_access");
        (string Tenant, string Subscription) haven;
        (string Tenant, string Subscription) events;
        await using (var seed = HarvestApiFixture.CreateHost(database))
        {
            using var client = seed.CreateClient();
            client.DefaultRequestHeaders.Add("X-Api-Key", HarvestApiFixture.OperatorKey);
            haven = await SubscriptionAsync(client, "haven");
            events = await SubscriptionAsync(client, "events");
        }

        using var rsa = RSA.Create(2048);
        var key = new RsaSecurityKey(rsa) { KeyId = "test-signing-key" };
        var grants = new Dictionary<string, object>
        {
            ["operator"] = new { isOperator = true, scopes = new[] { "tenants:manage" } },
            ["haven"] = new { tenantId = haven.Tenant, scopes = new[] { "events:read" } },
            ["events"] = new { tenantId = events.Tenant, scopes = new[] { "events:read" } },
        };
        await using var api = new WebApiTestHost<Program>
        {
            ConfigureConfigurationHook = configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseSettings:ConnectionString"] = database,
                ["Harvest:Worker:Enabled"] = "false",
                ["Harvest:Operator:ApiKey"] = HarvestApiFixture.OperatorKey,
                ["Harvest:ServiceAccess:Enabled"] = "true",
                ["Harvest:ServiceAccess:Authority"] = Issuer,
                ["Harvest:ServiceAccess:Audience"] = Audience,
                ["Harvest:ServiceAccess:ClientGrantsJson"] = JsonSerializer.Serialize(grants),
            }),
            ConfigureServicesHook = services => services.PostConfigure<JwtBearerOptions>("HarvestService", options =>
            {
                var configuration = new OpenIdConnectConfiguration { Issuer = Issuer };
                configuration.SigningKeys.Add(key);
                options.ConfigurationManager = new StaticConfigurationManager<OpenIdConnectConfiguration>(configuration);
            }),
        };

        using var apiClient = api.CreateClient();
        string havenFeed = $"/api/subscriptions/{haven.Subscription}/events";
        string eventsFeed = $"/api/subscriptions/{events.Subscription}/events";
        await ExpectAsync(apiClient, "/", HttpStatusCode.NotFound);
        await ExpectAsync(apiClient, "/api/identity/session", HttpStatusCode.NotFound);
        apiClient.DefaultRequestHeaders.Add("X-Api-Key", HarvestApiFixture.OperatorKey);
        await ExpectAsync(apiClient, "/api/tenants", HttpStatusCode.Unauthorized);
        apiClient.DefaultRequestHeaders.Remove("X-Api-Key");

        SetToken("haven", "events.read tenants.manage");
        await ExpectAsync(apiClient, havenFeed, HttpStatusCode.OK);
        await ExpectAsync(apiClient, eventsFeed, HttpStatusCode.NotFound);
        await ExpectAsync(apiClient, "/api/tenants", HttpStatusCode.Forbidden);
        SetToken("events", "events.read");
        await ExpectAsync(apiClient, eventsFeed, HttpStatusCode.OK);
        await ExpectAsync(apiClient, havenFeed, HttpStatusCode.NotFound);
        SetToken("operator", "tenants.manage");
        await ExpectAsync(apiClient, "/api/tenants", HttpStatusCode.OK);
        SetToken("operator", "events.read");
        await ExpectAsync(apiClient, "/api/tenants", HttpStatusCode.Forbidden);

        SetToken("haven", "events.read", audience: Audience.TrimEnd('/'));
        await ExpectAsync(apiClient, havenFeed, HttpStatusCode.Unauthorized);
        SetToken("haven", "events.read", issuer: "https://other.example.test/");
        await ExpectAsync(apiClient, havenFeed, HttpStatusCode.Unauthorized);
        SetToken("haven", "events.read", expired: true);
        await ExpectAsync(apiClient, havenFeed, HttpStatusCode.Unauthorized);
        SetToken("haven", "events.read", browserSession: true);
        await ExpectAsync(apiClient, havenFeed, HttpStatusCode.Forbidden);

        void SetToken(string clientId, string scope, string audience = Audience, string issuer = Issuer,
            bool expired = false, bool browserSession = false)
        {
            var now = DateTime.UtcNow;
            var claims = new Dictionary<string, object> { ["sub"] = clientId, ["client_id"] = clientId, ["scope"] = scope };
            if (browserSession)
                claims.Add("sid", "a-user-session");
            var token = new JsonWebTokenHandler().CreateToken(new SecurityTokenDescriptor
            {
                Issuer = issuer,
                Audience = audience,
                TokenType = "at+jwt",
                Claims = claims,
                NotBefore = now.AddMinutes(-5),
                Expires = expired ? now.AddMinutes(-2) : now.AddMinutes(5),
                SigningCredentials = new SigningCredentials(key, SecurityAlgorithms.RsaSha256),
            });
            apiClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        }
    }

    private static async Task<(string Tenant, string Subscription)> SubscriptionAsync(HttpClient client, string slug)
    {
        string tenantId = await RegisterAsync("/api/tenants", new { slug, name = slug });
        string containerId = await RegisterAsync("/api/containers", new { kind = "private", tenantId });
        string sourceId = await RegisterAsync("/api/sources", new
        {
            containerId, family = "site", handle = slug + ".example.test", coverage = "whole", completeness = "complete", posture = "ownFetch",
        });
        string pipelineId = await RegisterAsync("/api/pipelines", new { containerId, sourceId });
        return (tenantId, await RegisterAsync("/api/subscriptions", new { tenantId, containerId, pipelineId, backfill = "none" }));

        async Task<string> RegisterAsync(string path, object payload)
        {
            using var response = await client.PostAsJsonAsync(path, payload);
            response.StatusCode.Should().Be(HttpStatusCode.Created, await response.Content.ReadAsStringAsync());
            var json = await response.Content.ReadFromJsonAsync<JsonElement>();
            return json.GetProperty("data").GetProperty("id").GetString()!;
        }
    }

    private static async Task ExpectAsync(HttpClient client, string path, HttpStatusCode status)
    {
        using var response = await client.GetAsync(path);
        response.StatusCode.Should().Be(status, await response.Content.ReadAsStringAsync());
    }
}
