using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Runs;

namespace Harvest.Tests.Conformance.Runs;

public sealed class ModuleRunConformanceTests : RunBrokerConformance, IClassFixture<RunModuleFixture>, IDisposable
{
    private readonly AsyncServiceScope scope;
    public ModuleRunConformanceTests(RunModuleFixture fixture) => scope = fixture.Services.CreateAsyncScope();
    protected override IRegistrationBroker Registry => scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
    protected override IRunBroker Runs => scope.ServiceProvider.GetRequiredService<IRunBroker>();
    public void Dispose() => scope.Dispose();
}

public sealed class ClientRunConformanceTests(RunClientFixture fixture) : RunBrokerConformance, IClassFixture<RunClientFixture>
{
    protected override IRegistrationBroker Registry => fixture.Services.GetRequiredService<IRegistrationBroker>();
    protected override IRunBroker Runs => fixture.Services.GetRequiredService<IRunBroker>();
}

public sealed class FakeRunConformanceTests : RunBrokerConformance
{
    private readonly FakeRegistrationBroker registry = new();
    private FakeRunBroker? runs;
    protected override IRegistrationBroker Registry => registry;
    protected override IRunBroker Runs => runs ??= new FakeRunBroker(registry);
}
