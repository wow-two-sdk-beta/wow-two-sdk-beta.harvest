using AwesomeAssertions;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;

namespace Harvest.Tests.Integration.Registration;

/// <summary>The registry over a real store: the rows the schema's constraints accept, and the key store core's scheme
/// reads.</summary>
public sealed class ModuleRegistrationStoreTests
{
    private const string OperatorKey = "hv_IntegrationOperatorKey0000000000";

    [Fact]
    public async Task RegisterSource_ShouldStoreSnakeCaseTextAndJsonb_WhenTheSchemaChecksThem()
    {
        await using var host = await ModuleHost.StartAsync();
        var broker = Broker(host);
        var container = Value(await broker.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Pool }));

        var source = Value(await broker.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = container.Id,
            Family = SourceFamily.Youtube,
            Handle = "@WoWTwo",
            Coverage = SourceCoverage.Whole,
            Completeness = SourceCompleteness.Complete,
            Posture = SourcePosture.LicensedProvider,
            CheckPolicy = "none",
            CapabilityPolicy = new CapabilityPolicy { MonthUsd = 30m },
        }));

        (await host.ScalarAsync<string>($"SELECT family || '|' || posture || '|' || handle FROM harvest_sources WHERE id = '{source.Id}'"))
            .Should().Be("youtube|licensed_provider|wowtwo");
        (await host.ScalarAsync<string>($"SELECT capability_policy ->> 'monthUsd' FROM harvest_sources WHERE id = '{source.Id}'")).Should().Be("30");
        (await host.ScalarAsync<string>($"SELECT p.key FROM harvest_sources s JOIN harvest_check_policies p ON p.id = s.check_policy_id WHERE s.id = '{source.Id}'"))
            .Should().Be("none");
    }

    [Fact]
    public async Task DeclarePipeline_ShouldStoreScheduleRowsAndPendingState_WhenTheSourceIsRestricted()
    {
        await using var host = await ModuleHost.StartAsync();
        var broker = Broker(host);
        var tenant = Value(await broker.RegisterTenantAsync(new TenantRegistration { Slug = "events", Name = "Events" }));
        var container = Value(await broker.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = tenant.Id }));
        var source = Value(await broker.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = container.Id,
            Family = SourceFamily.Site,
            Handle = "afisha.uz",
            Coverage = SourceCoverage.Whole,
            Completeness = SourceCompleteness.Complete,
            Posture = SourcePosture.OwnFetch,
            Permission = SourcePermission.Restricted,
        }));

        var pipeline = Value(await broker.DeclarePipelineAsync(new PipelineDeclaration
        {
            ContainerId = container.Id,
            SourceId = source.Id,
            Schedules = [new ScheduleDeclaration { Kind = WorkKind.Discover, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(1) }],
        }));

        (await host.ScalarAsync<string>($"SELECT state || '|' || scope_key || '|' || ai_mode FROM harvest_pipelines WHERE id = '{pipeline.Id}'"))
            .Should().Be("pending_approval|*|off");
        (await host.ScalarAsync<string>($"SELECT kind || '|' || mode || '|' || every_interval::text FROM harvest_schedules WHERE pipeline_id = '{pipeline.Id}'"))
            .Should().Be("discover|interval|01:00:00");
    }

    [Fact]
    public async Task FindLiveByHash_ShouldAnswerALiveTenantKeyAndForgetItOnceRevoked_WhenTheSchemeAsks()
    {
        await using var host = await ModuleHost.StartAsync();
        var broker = Broker(host);
        var tenant = Value(await broker.RegisterTenantAsync(new TenantRegistration { Slug = "haven", Name = "Haven" }));
        var issued = Value(await broker.IssueApiKeyAsync(tenant.Id, new ApiKeyIssue { Name = "supply", Scopes = [HarvestScopeConstants.EventsRead] }));

        await using (var scope = host.Services.CreateAsyncScope())
        {
            var record = await scope.ServiceProvider.GetRequiredService<IApiKeyRepository>()
                .FindLiveByHashAsync(ApiKeySecretFactory.ToHash(issued.Secret), CancellationToken.None);
            record.Should().Match<ApiKeyRecord>(key => key.Id == issued.Key.Id.ToString() && key.Name == "supply");
            record!.Scopes.Should().Equal(HarvestScopeConstants.EventsRead);
            await scope.ServiceProvider.GetRequiredService<IApiKeyRepository>().TouchAsync(record.Id, host.Clock.GetUtcNow(), CancellationToken.None);
        }

        Value(await broker.GetApiKeyAsync(issued.Key.Id)).LastUsedAtUtc.Should().Be(host.Clock.GetUtcNow());
        Value(await broker.RevokeApiKeyAsync(tenant.Id, issued.Key.Id));

        await using var after = host.Services.CreateAsyncScope();
        (await after.ServiceProvider.GetRequiredService<IApiKeyRepository>()
            .FindLiveByHashAsync(ApiKeySecretFactory.ToHash(issued.Secret), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task FindLiveByHash_ShouldForgetAKey_WhenItExpired()
    {
        await using var host = await ModuleHost.StartAsync();
        var broker = Broker(host);
        var tenant = Value(await broker.RegisterTenantAsync(new TenantRegistration { Slug = "sift", Name = "Sift" }));
        var issued = Value(await broker.IssueApiKeyAsync(tenant.Id, new ApiKeyIssue
        {
            Name = "short",
            Scopes = [HarvestScopeConstants.PostsRead],
            ExpiresAtUtc = host.Clock.GetUtcNow().AddHours(1),
        }));

        host.Clock.Advance(TimeSpan.FromHours(2));

        await using var scope = host.Services.CreateAsyncScope();
        (await scope.ServiceProvider.GetRequiredService<IApiKeyRepository>()
            .FindLiveByHashAsync(ApiKeySecretFactory.ToHash(issued.Secret), CancellationToken.None)).Should().BeNull();
    }

    [Fact]
    public async Task FindLiveByHash_ShouldGrantEveryScopeToTheOperatorKey_WhenItIsConfigured()
    {
        await using var host = await ModuleHost.StartAsync(OperatorKey);

        await using var scope = host.Services.CreateAsyncScope();
        var record = await scope.ServiceProvider.GetRequiredService<IApiKeyRepository>()
            .FindLiveByHashAsync(ApiKeySecretFactory.ToHash(OperatorKey), CancellationToken.None);

        record!.Id.Should().Be(ApiKeyConstants.OperatorKeyId);
        record.Scopes.Should().BeEquivalentTo(HarvestScopeConstants.All);
    }

    private static IRegistrationBroker Broker(ModuleHost host) =>
        host.Services.CreateScope().ServiceProvider.GetRequiredService<IRegistrationBroker>();

    private static T Value<T>(Result<T> result) where T : notnull =>
        result is Result<T>.Success success
            ? success.Value
            : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);
}
