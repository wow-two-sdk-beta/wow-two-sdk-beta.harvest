using Harvest.Tests.Conformance.Posts;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Registration;

namespace Harvest.Tests.Conformance.Delivery;

/// <summary>Runs the shared preview rules over the PostgreSQL module.</summary>
public sealed class ModuleSubscriptionPreviewConformanceTests : SubscriptionPreviewConformance, IClassFixture<SelectorModuleFixture>, IDisposable
{
    private readonly SelectorModuleFixture fixture;
    private readonly AsyncServiceScope scope;
    public ModuleSubscriptionPreviewConformanceTests(SelectorModuleFixture fixture)
    {
        this.fixture = fixture; scope = fixture.Services.CreateAsyncScope();
    }
    protected override IRegistrationBroker Registry => scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
    protected override IPostBroker Posts => scope.ServiceProvider.GetRequiredService<IPostBroker>();
    protected override ISubscriptionPreviewBroker Preview => scope.ServiceProvider.GetRequiredService<ISubscriptionPreviewBroker>();
    protected override TimeProvider Clock => fixture.Clock;
    public void Dispose() => scope.Dispose();
}

/// <summary>Runs the same rules through the HTTP client and operator-authorized service routes.</summary>
public sealed class ClientSubscriptionPreviewConformanceTests(PostClientFixture fixture) : SubscriptionPreviewConformance, IClassFixture<PostClientFixture>
{
    protected override IRegistrationBroker Registry => fixture.Services.GetRequiredService<IRegistrationBroker>();
    protected override IPostBroker Posts => fixture.Services.GetRequiredService<IPostBroker>();
    protected override ISubscriptionPreviewBroker Preview => fixture.Services.GetRequiredService<ISubscriptionPreviewBroker>();
    protected override TimeProvider Clock => fixture.Clock;
}

/// <summary>Runs the same rules over the test kit's in-memory consumer fakes.</summary>
public sealed class FakeSubscriptionPreviewConformanceTests : SubscriptionPreviewConformance
{
    private static readonly FakeRegistrationBroker Registration = new(TimeProvider.System);
    private static readonly FakePostBroker PostStore = new(Registration);
    private static readonly FakeSubscriptionPreviewBroker PreviewStore = new(PostStore, Registration);
    protected override IRegistrationBroker Registry => Registration;
    protected override IPostBroker Posts => PostStore;
    protected override ISubscriptionPreviewBroker Preview => PreviewStore;
    protected override TimeProvider Clock => TimeProvider.System;
}
