using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Registration;

namespace Harvest.Tests.Conformance.Posts;

/// <summary>Runs the shared post and pull rules over the PostgreSQL module.</summary>
public sealed class ModulePostDeliveryConformanceTests : PostDeliveryBrokerConformance, IClassFixture<PostModuleFixture>, IDisposable
{
    private readonly PostModuleFixture fixture;
    private readonly AsyncServiceScope scope;
    public ModulePostDeliveryConformanceTests(PostModuleFixture fixture)
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
public sealed class ClientPostDeliveryConformanceTests(PostClientFixture fixture) : PostDeliveryBrokerConformance, IClassFixture<PostClientFixture>
{
    protected override IRegistrationBroker Registry => fixture.Services.GetRequiredService<IRegistrationBroker>();
    protected override IPostBroker Posts => fixture.Services.GetRequiredService<IPostBroker>();
    protected override IDeliveryBroker Delivery => fixture.Services.GetRequiredService<IDeliveryBroker>();
    protected override TimeProvider Clock => fixture.Clock;
}

/// <summary>Runs the same rules over the test kit's in-memory consumer fakes.</summary>
public sealed class FakePostDeliveryConformanceTests : PostDeliveryBrokerConformance
{
    private static readonly FakeRegistrationBroker Registration = new(TimeProvider.System);
    private static readonly FakePostBroker PostStore = new(Registration);
    private static readonly FakeDeliveryBroker DeliveryStore = new(PostStore, Registration);
    protected override IRegistrationBroker Registry => Registration;
    protected override IPostBroker Posts => PostStore;
    protected override IDeliveryBroker Delivery => DeliveryStore;
    protected override TimeProvider Clock => TimeProvider.System;
}
