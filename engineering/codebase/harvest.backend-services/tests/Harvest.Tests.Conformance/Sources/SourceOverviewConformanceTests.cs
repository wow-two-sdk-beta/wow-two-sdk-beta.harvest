using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Sources;

namespace Harvest.Tests.Conformance.Sources;

/// <summary>Runs the Sources reads over the PostgreSQL module.</summary>
public sealed class ModuleSourceOverviewConformanceTests : SourceOverviewBrokerConformance, IClassFixture<SourceModuleFixture>, IDisposable
{
    private readonly SourceModuleFixture fixture;
    private readonly AsyncServiceScope scope;

    public ModuleSourceOverviewConformanceTests(SourceModuleFixture fixture)
    {
        this.fixture = fixture;
        scope = fixture.Services.CreateAsyncScope();
    }

    protected override IRegistrationBroker Registry => scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
    protected override IRunBroker Runs => scope.ServiceProvider.GetRequiredService<IRunBroker>();
    protected override IPostBroker Posts => scope.ServiceProvider.GetRequiredService<IPostBroker>();
    protected override IDeliveryBroker Delivery => scope.ServiceProvider.GetRequiredService<IDeliveryBroker>();
    protected override ISourceOverviewBroker Overview => scope.ServiceProvider.GetRequiredService<ISourceOverviewBroker>();
    protected override TimeProvider Clock => fixture.Clock;
    public void Dispose() => scope.Dispose();
}

/// <summary>Runs the same reads through the HTTP client against the service's routes.</summary>
public sealed class ClientSourceOverviewConformanceTests(SourceClientFixture fixture) : SourceOverviewBrokerConformance, IClassFixture<SourceClientFixture>
{
    protected override IRegistrationBroker Registry => fixture.Services.GetRequiredService<IRegistrationBroker>();
    protected override IRunBroker Runs => fixture.Services.GetRequiredService<IRunBroker>();
    protected override IPostBroker Posts => fixture.Services.GetRequiredService<IPostBroker>();
    protected override IDeliveryBroker Delivery => fixture.Services.GetRequiredService<IDeliveryBroker>();
    protected override ISourceOverviewBroker Overview => fixture.Services.GetRequiredService<ISourceOverviewBroker>();
    protected override TimeProvider Clock => fixture.Clock;
}

/// <summary>Runs the same reads over the test kit's fakes.</summary>
public sealed class FakeSourceOverviewConformanceTests : SourceOverviewBrokerConformance
{
    private readonly FakeRegistrationBroker registry = new(TimeProvider.System);
    private readonly FakeRunBroker runs;
    private readonly FakePostBroker posts;
    private readonly FakeDeliveryBroker delivery;
    private readonly FakeSourceOverviewBroker overview;

    public FakeSourceOverviewConformanceTests()
    {
        runs = new FakeRunBroker(registry);
        posts = new FakePostBroker(registry);
        delivery = new FakeDeliveryBroker(posts, registry);
        overview = new FakeSourceOverviewBroker(registry, runs, posts, delivery);
    }

    protected override IRegistrationBroker Registry => registry;
    protected override IRunBroker Runs => runs;
    protected override IPostBroker Posts => posts;
    protected override IDeliveryBroker Delivery => delivery;
    protected override ISourceOverviewBroker Overview => overview;
    protected override TimeProvider Clock => TimeProvider.System;

    protected override void OnRunStarted(RunModel run, Guid sourceId) => posts.RegisterRun(run.Id, run.ContainerId, sourceId, run.PipelineId);
}
