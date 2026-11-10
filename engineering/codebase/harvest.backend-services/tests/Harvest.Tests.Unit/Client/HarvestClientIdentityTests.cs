using System.Buffers.Text;
using System.Net;
using System.Text;
using System.Text.Json;
using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Client;
using WoW.Two.Sdk.Beta.Be.Harvest.Client.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Client.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Http.Auth.OAuth2;
using WoW.Two.Sdk.Beta.Be.Identity.Testing;

namespace Harvest.Tests.Unit.Client;

/// <summary>The client's credential: a client-credentials token from Identity, or the API key, on every broker.</summary>
public sealed class HarvestClientIdentityTests : IDisposable
{
    private const string ClientId = "events";
    private const string Resource = "https://scfd.test/";
    private static readonly Uri Root = new("https://scfd.test");

    private readonly FakeTokenIssuer issuer = new();
    private readonly ScfdHandler service = new();

    [Fact]
    public async Task Pull_ShouldSendAClientCredentialsToken_ForTheServiceRoot()
    {
        await using var provider = Provider(options => options.Identity = Identity());

        await provider.GetRequiredService<IDeliveryBroker>().PullAsync(Guid.NewGuid());

        var call = service.Calls.Single();
        call.ApiKey.Should().BeNull();
        var claims = Claims(call.Bearer);
        claims.GetProperty("sub").GetString().Should().Be(ClientId);
        claims.GetProperty("aud").GetString().Should().Be(Resource, "the resource defaults to the service root with its slash");
        claims.GetProperty("scope").GetString().Should().Be("harvest.events-read");
    }

    [Fact]
    public async Task EveryBroker_ShouldTakeTheSameToken_WhenAddHarvestClientNamesIdentity()
    {
        await using var provider = Provider(options => options.Identity = Identity());

        await provider.GetRequiredService<IRegistrationBroker>().ListTenantsAsync();
        await provider.GetRequiredService<IRunBroker>().GetAsync(Guid.NewGuid());
        await provider.GetRequiredService<IDeliveryBroker>().PullAsync(Guid.NewGuid());

        service.Calls.Should().HaveCount(3).And.OnlyContain(call => call.Bearer != null && call.ApiKey == null);
    }

    [Fact]
    public async Task Pull_ShouldSendTheApiKey_WhenTheClientHasOne()
    {
        await using var provider = Provider(options => options.ApiKey = "hv_UnitTestKey000000000000000000000");

        await provider.GetRequiredService<IDeliveryBroker>().PullAsync(Guid.NewGuid());

        service.Calls.Single().Should().Be(new ScfdCall(null, "hv_UnitTestKey000000000000000000000"));
    }

    [Fact]
    public void Options_ShouldFailValidation_WhenBothApiKeyAndIdentityAreSet()
    {
        using var provider = Provider(options =>
        {
            options.ApiKey = "hv_UnitTestKey000000000000000000000";
            options.Identity = Identity();
        });

        FluentActions.Invoking(() => provider.GetRequiredService<IOptions<HarvestClientOptions>>().Value)
            .Should().Throw<OptionsValidationException>()
            .WithMessage("*set ApiKey or Identity, not both*");
    }

    [Fact]
    public void ToIdentityScope_ShouldNameEveryScopeUnderTheHarvestResource()
    {
        HarvestScopeConstants.All.Select(HarvestScopeConstants.ToIdentityScope).Should().Equal(
            "harvest.tenants-manage", "harvest.containers-manage", "harvest.sources-manage", "harvest.pipelines-read",
            "harvest.pipelines-run", "harvest.pipelines-manage", "harvest.subscriptions-manage", "harvest.events-read",
            "harvest.links-submit", "harvest.entries-write", "harvest.posts-read");
    }

    public void Dispose() => issuer.Dispose();

    private ServiceProvider Provider(Action<HarvestClientOptions> configure)
    {
        var services = new ServiceCollection().AddLogging();
        services.AddHarvestClient(options =>
            {
                options.BaseAddress = Root;
                configure(options);
            })
            .ConfigurePrimaryHttpMessageHandler(() => service);
        services.AddHarvestDataClient();
        services.AddHarvestRunClient();
        services.AddHttpClient<IDeliveryBroker, ApiDeliveryBroker>().ConfigurePrimaryHttpMessageHandler(() => service);
        services.AddHttpClient<IRunBroker, ApiRunBroker>().ConfigurePrimaryHttpMessageHandler(() => service);
        services.AddHttpClient(OAuth2HttpClientNames.TokenEndpoint).ConfigurePrimaryHttpMessageHandler(issuer.CreateTokenEndpointHandler);
        return services.BuildServiceProvider(validateScopes: true);
    }

    private static HarvestClientIdentityOptions Identity() => new()
    {
        TokenEndpoint = new Uri("https://identity.test/connect/token"),
        ClientId = ClientId,
        ClientSecret = "test-secret",
        Scope = "harvest.events-read",
    };

    private static JsonElement Claims(string? bearer)
    {
        bearer.Should().NotBeNullOrEmpty();
        return JsonDocument.Parse(Base64Url.DecodeFromChars(bearer!.Split('.')[1])).RootElement;
    }

    /// <summary>Represents what one call brought to SCFD.</summary>
    private sealed record ScfdCall(string? Bearer, string? ApiKey);

    /// <summary>Stands in for SCFD: records each call's credentials and answers with an empty page.</summary>
    private sealed class ScfdHandler : HttpMessageHandler
    {
        public List<ScfdCall> Calls { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var bearer = request.Headers.Authorization is { Scheme: "Bearer" } header ? header.Parameter : null;
            var apiKey = request.Headers.TryGetValues("X-Api-Key", out var keys) ? keys.Single() : null;
            Calls.Add(new ScfdCall(bearer, apiKey));
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"data":[],"next":"","hasMore":false}""", Encoding.UTF8, "application/json"),
            });
        }
    }
}
