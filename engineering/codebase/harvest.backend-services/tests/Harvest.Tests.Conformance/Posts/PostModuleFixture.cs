using Harvest.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;

namespace Harvest.Tests.Conformance.Posts;

public sealed class PostModuleFixture : IAsyncLifetime
{
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));
    public ServiceProvider Services { get; private set; } = null!;
    async Task IAsyncLifetime.InitializeAsync()
    {
        var connectionString = (await PostgresServer.SharedAsync()).NewDatabase("post_conformance");
        var services = new ServiceCollection().AddLogging().AddSingleton<TimeProvider>(Clock);
        services.AddHarvest().Configure(options => options.ConnectionString = connectionString);
        services.AddHarvestPosts().AddHarvestDelivery();
        Services = services.BuildServiceProvider(validateScopes: true);
        await Services.MigrateHarvestOnStartupAsync("test");
    }
    async Task IAsyncLifetime.DisposeAsync() => await Services.DisposeAsync();
}
