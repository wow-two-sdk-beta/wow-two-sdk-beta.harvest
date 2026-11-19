using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Harvest.Entities;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Entities;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Registration;

namespace Harvest.Tests.Conformance.Entities;

/// <summary>Runs the entity reads' rules over the PostgreSQL module.</summary>
public sealed class ModuleEntitiesConformanceTests : EntitiesBrokerConformance, IClassFixture<EntityModuleFixture>, IDisposable
{
    private readonly EntityModuleFixture fixture;
    private readonly AsyncServiceScope scope;

    public ModuleEntitiesConformanceTests(EntityModuleFixture fixture)
    {
        this.fixture = fixture;
        scope = fixture.Services.CreateAsyncScope();
    }

    protected override IRegistrationBroker Registry => scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
    protected override IPostBroker Posts => scope.ServiceProvider.GetRequiredService<IPostBroker>();
    protected override IEntitiesBroker Entities => scope.ServiceProvider.GetRequiredService<IEntitiesBroker>();
    protected override TimeProvider Clock => fixture.Clock;
    protected override Task SeedAsync(EntitySeed seed) => EntitySeedWriter.WriteAsync(fixture.ConnectionString, seed);
    public void Dispose() => scope.Dispose();
}

/// <summary>Runs the same rules through the HTTP client and the service's <c>api/entities</c> routes.</summary>
public sealed class ClientEntitiesConformanceTests(EntityClientFixture fixture) : EntitiesBrokerConformance, IClassFixture<EntityClientFixture>
{
    protected override IRegistrationBroker Registry => fixture.Services.GetRequiredService<IRegistrationBroker>();
    protected override IPostBroker Posts => fixture.Services.GetRequiredService<IPostBroker>();
    protected override IEntitiesBroker Entities => fixture.Services.GetRequiredService<IEntitiesBroker>();
    protected override TimeProvider Clock => fixture.Clock;
    protected override Task SeedAsync(EntitySeed seed) => EntitySeedWriter.WriteAsync(fixture.ConnectionString, seed);
}

/// <summary>Runs the same rules over the test kit's in-memory fakes.</summary>
public sealed class FakeEntitiesConformanceTests : EntitiesBrokerConformance
{
    private readonly FakeRegistrationBroker registry = new(TimeProvider.System);
    private readonly FakePostBroker posts;
    private readonly FakeEntitiesBroker entities;

    public FakeEntitiesConformanceTests()
    {
        posts = new FakePostBroker(registry);
        entities = new FakeEntitiesBroker(registry, posts);
    }

    protected override IRegistrationBroker Registry => registry;
    protected override IPostBroker Posts => posts;
    protected override IEntitiesBroker Entities => entities;
    protected override TimeProvider Clock => TimeProvider.System;

    protected override Task SeedAsync(EntitySeed seed)
    {
        entities.Seed(seed);
        return Task.CompletedTask;
    }
}
