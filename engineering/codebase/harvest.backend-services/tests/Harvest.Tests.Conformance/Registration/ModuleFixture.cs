using Harvest.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;

namespace Harvest.Tests.Conformance.Registration;

/// <summary>Form 1: the module wired as a consumer wires it, over a fresh PostgreSQL database its own boot creates.</summary>
public sealed class ModuleFixture : IAsyncLifetime
{
    /// <summary>Gets the clock the module stamps rows with.</summary>
    public FakeTimeProvider Clock { get; } = new(DateTimeOffset.UnixEpoch.AddDays(1));

    /// <summary>Gets the consumer's container.</summary>
    public ServiceProvider Services { get; private set; } = null!;

    async Task IAsyncLifetime.InitializeAsync()
    {
        var connectionString = (await PostgresServer.SharedAsync()).NewDatabase("conformance_module");
        var services = new ServiceCollection().AddLogging().AddSingleton<TimeProvider>(Clock);
        services.AddHarvest().Configure(options => options.ConnectionString = connectionString);
        Services = services.BuildServiceProvider(validateScopes: true);
        await Services.MigrateHarvestOnStartupAsync("test");
    }

    async Task IAsyncLifetime.DisposeAsync() => await Services.DisposeAsync();
}
