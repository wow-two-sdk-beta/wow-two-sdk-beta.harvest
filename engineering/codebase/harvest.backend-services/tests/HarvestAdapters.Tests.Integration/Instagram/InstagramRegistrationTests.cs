using AwesomeAssertions;
using HarvestAdapters.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;

namespace HarvestAdapters.Tests.Integration.Instagram;

[Collection(SourceDeliveryCollection.Name)]
public sealed class InstagramRegistrationTests
{
    [Theory]
    [InlineData("instagram.com")]
    [InlineData("www.instagram.com")]
    public async Task Profiles_and_limits_are_explicit_registry_rows_independent_of_connection_settings(string host)
    {
        var providerCalls = new NoCollectionBroker();
        var services = new ServiceCollection().AddLogging();
        string database = await SourcePostgres.NewDatabaseAsync();
        services.AddHarvest().Configure(options => options.ConnectionString = database);
        services.AddHarvestInstagram();
        services.AddSingleton<IInstagramMediaBroker>(providerCalls);
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await provider.MigrateHarvestOnStartupAsync("instagram-registration-fixture");
        await using var scope = provider.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var tenant = Value(await registry.RegisterTenantAsync(new TenantRegistration { Slug = "registry-consumer", Name = "Registry consumer" }));
        var container = Value(await registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Private, TenantId = tenant.Id }));
        var source = Value(await registry.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = container.Id, Family = SourceFamily.Instagram, Handle = host,
            Coverage = SourceCoverage.Filtered, Completeness = SourceCompleteness.Window, Posture = SourcePosture.Official,
            AiAllowed = false, PoolShareable = false, CheckPolicy = "none",
        }));
        Value(await registry.ListPipelinesAsync(container.Id)).Should().BeEmpty();
        var first = Value(await registry.DeclarePipelineAsync(new()
        {
            ContainerId = container.Id, SourceId = source.Id, Name = "Saved publisher",
            Scope = new() { Url = $"https://{host}/custom_publisher/" },
            Limits = new() { PageSize = 5, MaxPages = 3 },
        }));
        first.State.Should().Be(PipelineState.Active);
        first.Scope.RestrictToPath.Should().BeFalse();
        first.Schedules.Should().BeEmpty();
        first.Ai.Should().Be(AiMode.Off);
        first.Pinned.Should().BeFalse();

        Value(await registry.UpdatePipelineAsync(first.Id, new()
        {
            Name = "My saved publisher", Scope = first.Scope,
            Limits = new() { PageSize = 7, MaxPages = 2 },
            Schedules = [new() { Kind = WorkKind.Capture, Mode = ScheduleMode.Interval, Every = TimeSpan.FromHours(12), Enabled = false }],
        }));
        await provider.ReconcileHarvestSourceSectionsAsync(CancellationToken.None);
        await provider.ReconcileHarvestSourceSectionsAsync(CancellationToken.None);
        Value(await registry.ListPipelinesAsync(container.Id)).Should().ContainSingle();
        Value(await registry.DeclarePipelineAsync(new()
        {
            ContainerId = container.Id, SourceId = source.Id,
            Scope = new() { Url = $"https://{host}/another.publisher/" },
            Limits = new() { PageSize = 11, MaxPages = 4 },
        }));

        var saved = Value(await registry.GetPipelineAsync(first.Id));
        saved.Name.Should().Be("My saved publisher");
        saved.Scope.Url.Should().Be($"https://{host}/custom_publisher/");
        saved.Limits.PageSize.Should().Be(7);
        saved.Limits.MaxPages.Should().Be(2);
        saved.Schedules.Should().ContainSingle().Which.Every.Should().Be(TimeSpan.FromHours(12));
        saved.Schedules.Should().OnlyContain(schedule => !schedule.Enabled);
        Value(await registry.ListPipelinesAsync(container.Id)).Should().HaveCount(2);

        (await registry.DeclarePipelineAsync(new()
        {
            ContainerId = container.Id, SourceId = source.Id,
            Scope = new() { Url = $"https://{host}/third.publisher/" }, Limits = new() { PageSize = 51 },
        })).IsSuccess.Should().BeFalse();
        (await registry.DeclarePipelineAsync(new()
        {
            ContainerId = container.Id, SourceId = source.Id, Scope = new() { Url = $"https://{host}/p/Post123/" },
        })).IsSuccess.Should().BeFalse();
        (await registry.UpdatePipelineAsync(first.Id, new()
        {
            Scope = first.Scope, Schedules = [], Limits = new() { PageSize = 51 },
        })).IsSuccess.Should().BeFalse();
        Value(await registry.GetPipelineAsync(first.Id)).Should().BeEquivalentTo(saved);
        Value(await registry.ListPipelinesAsync(container.Id)).Should().HaveCount(2);
        Value(await registry.ListTenantsAsync()).Should().ContainSingle().Which.Id.Should().Be(tenant.Id);
        providerCalls.Calls.Should().Be(0);
    }

    private static T Value<T>(Result<T> result) where T : notnull => result is Result<T>.Success success ? success.Value
        : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);

    private sealed class NoCollectionBroker : IInstagramMediaBroker
    {
        public int Calls { get; private set; }
        public Task<InstagramMediaPage> ReadAsync(string username, int pageSize = 25, string? after = null, CancellationToken cancellationToken = default)
        {
            Calls++;
            throw new InvalidOperationException("Source registration must not call a provider.");
        }
    }
}
