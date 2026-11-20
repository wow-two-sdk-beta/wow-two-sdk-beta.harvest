using AwesomeAssertions;
using Harvest.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace Harvest.Tests.Integration.Catalog;

/// <summary>Boot staging of source sections for a source whose entry the instance turned off: <c>ReconcileHarvestSourceSectionsAsync</c>
/// adds the adapter's dormant section pipelines to every source but that one, and stages it once its entry is on.</summary>
public sealed class SourceCatalogSectionsTests
{
    [Fact]
    public async Task BootStaging_ShouldSkipASourceTheInstanceTurnedOff_AndStageItOnceItsEntryIsOn()
    {
        string database = (await PostgresServer.SharedAsync()).NewDatabase("catalog-sections");
        var adapter = new SectionAdapter();
        var services = new ServiceCollection().AddLogging().AddSingleton<TimeProvider>(TimeProvider.System).AddSingleton<ISourceAdapter>(adapter);
        services.AddHarvest().Configure(options => options.ConnectionString = database);
        await using var provider = services.BuildServiceProvider(validateScopes: true);
        await provider.MigrateHarvestOnStartupAsync("catalog-sections");
        await using var scope = provider.CreateAsyncScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        var container = Value(await registry.RegisterContainerAsync(new ContainerRegistration { Kind = ContainerKind.Pool })).Id;
        var off = await RegisterAsync(registry, container, "offpublisher");
        var on = await RegisterAsync(registry, container, "onpublisher");
        (await PipelineSourcesAsync(registry, container)).Should().BeEmpty("the adapter declared no sections when the sources were registered");
        await CatalogSql.TurnOffAsync(database, off);
        adapter.Declares = true;

        await provider.ReconcileHarvestSourceSectionsAsync(default);

        (await PipelineSourcesAsync(registry, container)).Should().Equal([on], "the off source is skipped at boot");

        await CatalogSql.SetAsync(database, CatalogSql.Key, true);
        await provider.ReconcileHarvestSourceSectionsAsync(default);

        (await PipelineSourcesAsync(registry, container)).Should().BeEquivalentTo([on, off], "turning the entry on stages it at the next boot");
    }

    private static async Task<Guid> RegisterAsync(IRegistrationBroker registry, Guid container, string handle) =>
        Value(await registry.RegisterSourceAsync(new SourceRegistration
        {
            ContainerId = container, Family = SourceFamily.Instagram, Handle = handle,
            Coverage = SourceCoverage.Filtered, Completeness = SourceCompleteness.Window, Posture = SourcePosture.Official,
        })).Id;

    private static async Task<Guid[]> PipelineSourcesAsync(IRegistrationBroker registry, Guid container) =>
        [.. Value(await registry.ListPipelinesAsync(container)).Select(pipeline => pipeline.SourceId)];

    private static T Value<T>(Result<T> result) where T : notnull => result is Result<T>.Success success ? success.Value
        : throw new InvalidOperationException(((Result<T>.Failure)result).Error.Message);

    /// <summary>An adapter for Instagram sources that declares one passive section once <see cref="Declares"/> is on.</summary>
    private sealed class SectionAdapter : ISourceAdapter, ISourceSectionCatalog
    {
        internal bool Declares { get; set; }

        public string Key => "section-fixture";

        public SourceFamily Family => SourceFamily.Instagram;

        public bool Serves(SourceModel source) => source.Family == Family;

        public AdapterDescriptor Describe(SourceModel source) => new()
        {
            SourceUrl = $"https://www.instagram.com/{source.Handle}/", HostKey = "www.instagram.com",
            Steps = [new() { Key = "profile", Role = StepRole.Enumerate }],
            Plans = new Dictionary<WorkKind, IReadOnlyList<string>> { [WorkKind.Capture] = ["profile"] },
        };

        IReadOnlyList<SourceSectionDefinition> ISourceSectionCatalog.Sections(SourceModel source) => Declares
            ? [new("Profile", Describe(source).SourceUrl!, "en") { RestrictToPath = false }] : [];
    }
}
