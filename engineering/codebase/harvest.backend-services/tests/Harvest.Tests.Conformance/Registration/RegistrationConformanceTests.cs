using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Registration;

namespace Harvest.Tests.Conformance.Registration;

/// <summary>Form 1: the contract's suite against the in-process module.</summary>
public sealed class ModuleRegistrationConformanceTests : RegistrationBrokerConformance, IClassFixture<ModuleFixture>, IDisposable
{
    private readonly ModuleFixture fixture;
    private readonly AsyncServiceScope scope;

    public ModuleRegistrationConformanceTests(ModuleFixture fixture)
    {
        this.fixture = fixture;
        scope = fixture.Services.CreateAsyncScope();
    }

    protected override IRegistrationBroker Broker => scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();

    protected override TimeProvider Clock => fixture.Clock;

    public void Dispose() => scope.Dispose();
}

/// <summary>Forms 2 and 3: the contract's suite through the HTTP client against the service host.</summary>
public sealed class ClientRegistrationConformanceTests(ServiceFixture fixture)
    : RegistrationBrokerConformance, IClassFixture<ServiceFixture>
{
    protected override IRegistrationBroker Broker => fixture.Services.GetRequiredService<IRegistrationBroker>();

    protected override TimeProvider Clock => fixture.Clock;
}

/// <summary>The test kit's fake keeps the contract too, so a consumer's tests rely on it as on the real thing.</summary>
public sealed class FakeRegistrationConformanceTests : RegistrationBrokerConformance
{
    private static readonly FakeRegistrationBroker Shared = new(TimeProvider.System);

    protected override IRegistrationBroker Broker => Shared;

    protected override TimeProvider Clock => TimeProvider.System;
}
