using Harvest.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;

namespace Harvest.Tests.Conformance.Entities;

/// <summary>Form 1: the module's registry, posts and entity reads over one fresh PostgreSQL database.</summary>
public sealed class EntityModuleFixture : IAsyncLifetime
{
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 19, 9, 0, 0, TimeSpan.Zero));
    public ServiceProvider Services { get; private set; } = null!;
    public string ConnectionString { get; private set; } = null!;

    async Task IAsyncLifetime.InitializeAsync()
    {
        ConnectionString = (await PostgresServer.SharedAsync()).NewDatabase("entity_conformance");
        var services = new ServiceCollection().AddLogging().AddSingleton<TimeProvider>(Clock);
        services.AddHarvest().Configure(options => options.ConnectionString = ConnectionString);
        services.AddHarvestPosts().AddHarvestEntityReads();
        Services = services.BuildServiceProvider(validateScopes: true);
        await Services.MigrateHarvestOnStartupAsync("test");
    }

    async Task IAsyncLifetime.DisposeAsync() => await Services.DisposeAsync();
}
