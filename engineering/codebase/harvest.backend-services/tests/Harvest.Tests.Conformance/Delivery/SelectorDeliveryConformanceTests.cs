using Harvest.Tests.Conformance.Posts;
using Harvest.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Registration;

namespace Harvest.Tests.Conformance.Delivery;

/// <summary>Hosts the module with posts, delivery and the selector preview over a fresh PostgreSQL database.</summary>
public sealed class SelectorModuleFixture : IAsyncLifetime
{
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));
    public ServiceProvider Services { get; private set; } = null!;
    async Task IAsyncLifetime.InitializeAsync()
    {
        var connectionString = (await PostgresServer.SharedAsync()).NewDatabase("selector_conformance");
        var services = new ServiceCollection().AddLogging().AddSingleton<TimeProvider>(Clock);
        services.AddHarvest().Configure(options => options.ConnectionString = connectionString);
        services.AddHarvestPosts().AddHarvestDelivery().AddHarvestSubscriptionPreview();
        Services = services.BuildServiceProvider(validateScopes: true);
        await Services.MigrateHarvestOnStartupAsync("test");
    }
    async Task IAsyncLifetime.DisposeAsync() => await Services.DisposeAsync();
}

/// <summary>Runs the shared selector rules over the PostgreSQL module.</summary>
public sealed class ModuleSelectorDeliveryConformanceTests : SelectorDeliveryConformance, IClassFixture<SelectorModuleFixture>, IDisposable
{
    private readonly SelectorModuleFixture fixture;
    private readonly AsyncServiceScope scope;
    public ModuleSelectorDeliveryConformanceTests(SelectorModuleFixture fixture)
    {
        this.fixture = fixture; scope = fixture.Services.CreateAsyncScope();
    }
    protected override IRegistrationBroker Registry => scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
    protected override IPostBroker Posts => scope.ServiceProvider.GetRequiredService<IPostBroker>();
    protected override IDeliveryBroker Delivery => scope.ServiceProvider.GetRequiredService<IDeliveryBroker>();
    protected override TimeProvider Clock => fixture.Clock;
    public void Dispose() => scope.Dispose();
}

/// <summary>Runs the same rules through the HTTP client and operator-authorized service routes.</summary>
public sealed class ClientSelectorDeliveryConformanceTests(PostClientFixture fixture) : SelectorDeliveryConformance, IClassFixture<PostClientFixture>
{
    protected override IRegistrationBroker Registry => fixture.Services.GetRequiredService<IRegistrationBroker>();
    protected override IPostBroker Posts => fixture.Services.GetRequiredService<IPostBroker>();
    protected override IDeliveryBroker Delivery => fixture.Services.GetRequiredService<IDeliveryBroker>();
    protected override TimeProvider Clock => fixture.Clock;
}

/// <summary>Runs the same rules over the test kit's in-memory consumer fakes.</summary>
public sealed class FakeSelectorDeliveryConformanceTests : SelectorDeliveryConformance
{
    private static readonly FakeRegistrationBroker Registration = new(TimeProvider.System);
    private static readonly FakePostBroker PostStore = new(Registration);
    private static readonly FakeDeliveryBroker DeliveryStore = new(PostStore, Registration);
    protected override IRegistrationBroker Registry => Registration;
    protected override IPostBroker Posts => PostStore;
    protected override IDeliveryBroker Delivery => DeliveryStore;
    protected override TimeProvider Clock => TimeProvider.System;
}
