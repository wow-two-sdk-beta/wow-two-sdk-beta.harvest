using System.Net;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;
using WoW.Two.Sdk.Beta.Be.Integrations;
using WoW.Two.Sdk.Beta.Be.Integrations.Instagram;

namespace HarvestAdapters.Tests.Unit.Instagram;

public sealed class InstagramRegistrationTests
{
    [Fact]
    public async Task Missing_credentials_resolve_worker_brokers_without_sending_or_claiming_readiness()
    {
        var handler = new RecordingHandler();
        var services = Services(handler, options => options.AccessToken = "");
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await using AsyncServiceScope scope = provider.CreateAsyncScope();

        IStepBroker step = Assert.Single(scope.ServiceProvider.GetServices<IStepBroker>());
        var result = await scope.ServiceProvider.GetRequiredService<IInstagramMediaBroker>().ReadAsync("approved");

        Assert.IsType<InstagramStepBroker>(step);
        Assert.Equal(FetchOutcome.CapabilityUnavailable, result.Outcome);
        Assert.Equal(SourceReadiness.ConfigurationRequired,
            ((ISourceConfigurationProvider)Assert.Single(provider.GetServices<ISourceAdapter>())).DescribeConfiguration(Source()).Readiness);
        Assert.Equal(TimeSpan.FromSeconds(30), provider.GetRequiredService<InstagramIntegrationOptions>().Timeout);
        Assert.Equal(2_000_000, provider.GetRequiredService<InstagramIntegrationOptions>().MaximumResponseBytes);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Invalid_origin_is_not_ready_and_never_reaches_the_transport()
    {
        var handler = new RecordingHandler();
        var services = Services(handler, options => options.BaseAddress = "https://other.example/");
        await using ServiceProvider provider = services.BuildServiceProvider();

        Assert.Equal(SourceReadiness.ConfigurationRequired,
            ((ISourceConfigurationProvider)Assert.Single(provider.GetServices<ISourceAdapter>())).DescribeConfiguration(Source()).Readiness);
        Assert.Equal(new Uri("https://graph.facebook.com/"), provider.GetRequiredService<InstagramIntegrationOptions>().BaseAddress);
        Assert.Equal(FetchOutcome.CapabilityUnavailable,
            (await provider.GetRequiredService<IInstagramMediaBroker>().ReadAsync("approved")).Outcome);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Missing_account_does_not_claim_readiness_or_send()
    {
        var handler = new RecordingHandler();
        var services = Services(handler, options => options.ProfessionalAccountId = "");
        await using ServiceProvider provider = services.BuildServiceProvider();

        var result = await provider.GetRequiredService<IInstagramMediaBroker>().ReadAsync("approved");

        Assert.Equal(FetchOutcome.CapabilityUnavailable, result.Outcome);
        Assert.Equal(SourceReadiness.ConfigurationRequired,
            ((ISourceConfigurationProvider)Assert.Single(provider.GetServices<ISourceAdapter>())).DescribeConfiguration(Source()).Readiness);
        Assert.Equal(0, handler.Calls);
    }

    [Fact]
    public async Task Worker_scopes_get_distinct_brokers_and_only_the_Instagram_credential_source_is_used()
    {
        var handler = new RecordingHandler();
        var unrelated = new UnrelatedTokenSource();
        var services = Services(handler);
        services.AddSingleton<IAccessTokenService>(unrelated);
        await using ServiceProvider provider = services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
        await using AsyncServiceScope first = provider.CreateAsyncScope();
        await using AsyncServiceScope second = provider.CreateAsyncScope();

        Assert.NotSame(Assert.Single(first.ServiceProvider.GetServices<IStepBroker>()),
            Assert.Single(second.ServiceProvider.GetServices<IStepBroker>()));
        var result = await first.ServiceProvider.GetRequiredService<IInstagramMediaBroker>().ReadAsync("approved");

        Assert.Equal(FetchOutcome.Ok, result.Outcome);
        Assert.Equal("Bearer synthetic-instagram-token", handler.Authorization);
        Assert.Equal(1, handler.Calls);
        Assert.Equal(0, unrelated.Calls);
    }

    private static ServiceCollection Services(RecordingHandler handler, Action<InstagramOptions>? configure = null)
    {
        var services = new ServiceCollection();
        services.AddHarvestInstagram().Configure(options =>
        {
            options.AccessToken = "synthetic-instagram-token";
            options.ProfessionalAccountId = "123456789";
            options.ApiVersion = "v1.0";
            configure?.Invoke(options);
        });
        services.AddHttpClient("WoW.Two.Integrations.Instagram").ConfigurePrimaryHttpMessageHandler(() => handler);
        return services;
    }

    private static SourceModel Source() => new()
    {
        Id = Guid.NewGuid(), ContainerId = Guid.NewGuid(), Family = SourceFamily.Instagram, Handle = "www.instagram.com",
        Coverage = SourceCoverage.Filtered, Completeness = SourceCompleteness.Window, Posture = SourcePosture.Official,
        Permission = SourcePermission.Open, AiAllowed = false, PoolShareable = false, RobotsPolicy = RobotsPolicy.Obey,
        ConsentBasis = ConsentBasis.None, Authority = 50, RetentionClass = "standard", CapabilityPolicy = new(),
        State = SourceState.Active, StateSinceUtc = DateTimeOffset.UnixEpoch, CreatedAtUtc = DateTimeOffset.UnixEpoch,
    };

    private sealed class UnrelatedTokenSource : IAccessTokenService
    {
        public int Calls { get; private set; }
        public Task<string?> GetAccessTokenAsync(CancellationToken ct = default)
        {
            Calls++;
            return Task.FromResult<string?>("unrelated-provider-token");
        }
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public int Calls { get; private set; }
        public string? Authorization { get; private set; }
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Calls++;
            Authorization = request.Headers.Authorization?.ToString();
            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("""{"business_discovery":{"username":"approved","media":{"data":[]}}}"""),
            });
        }
    }
}
