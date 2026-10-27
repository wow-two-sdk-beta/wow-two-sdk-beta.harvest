using Harvest.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;

namespace Harvest.Tests.Conformance.Runs;

public sealed class RunModuleFixture : IAsyncLifetime
{
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));
    public ServiceProvider Services { get; private set; } = null!;

    async Task IAsyncLifetime.InitializeAsync()
    {
        string connectionString = (await PostgresServer.SharedAsync()).NewDatabase("run_conformance");
        var services = new ServiceCollection().AddLogging().AddSingleton<TimeProvider>(Clock);
        services.AddHarvest().Configure(options => options.ConnectionString = connectionString);
        services.AddHarvestFetching();
        services.AddHarvestReaders().AddHarvestExtraction().AddHarvestPosts().AddHarvestDelivery();
        services.AddHarvestEngine();
        Services = services.BuildServiceProvider(validateScopes: true);
        await Services.MigrateHarvestOnStartupAsync("test");
    }

    async Task IAsyncLifetime.DisposeAsync() => await Services.DisposeAsync();
}
