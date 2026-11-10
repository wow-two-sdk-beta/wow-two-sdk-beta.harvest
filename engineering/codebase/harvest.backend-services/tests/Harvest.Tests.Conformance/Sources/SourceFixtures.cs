using Harvest.Api.Configurations;
using Harvest.Tests.Conformance.Registration;
using Harvest.Tests.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using Microsoft.Extensions.Time.Testing;
using WoW.Two.Sdk.Beta.Be.Harvest.Client;
using WoW.Two.Sdk.Beta.Be.Harvest.Client.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Client.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Client.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Sources;
using WoW.Two.Sdk.Beta.Be.Testing;

namespace Harvest.Tests.Conformance.Sources;

/// <summary>Form 1: the module with every broker the overview reads over, and no worker.</summary>
public sealed class SourceModuleFixture : IAsyncLifetime
{
    public FakeTimeProvider Clock { get; } = new(new DateTimeOffset(2026, 11, 10, 9, 0, 0, TimeSpan.Zero));
    public ServiceProvider Services { get; private set; } = null!;

    async Task IAsyncLifetime.InitializeAsync()
    {
        string connectionString = (await PostgresServer.SharedAsync()).NewDatabase("source_conformance");
        var services = new ServiceCollection().AddLogging().AddSingleton<TimeProvider>(Clock);
        services.AddHarvest().Configure(options => options.ConnectionString = connectionString);
        services.AddHarvestFetching();
        services.AddHarvestReaders().AddHarvestExtraction().AddHarvestPosts().AddHarvestDelivery().AddHarvestSources();
        services.AddHarvestEngine();
        Services = services.BuildServiceProvider(validateScopes: true);
        await Services.MigrateHarvestOnStartupAsync("test");
    }

    async Task IAsyncLifetime.DisposeAsync() => await Services.DisposeAsync();
}

/// <summary>Forms 2 and 3: every HTTP broker against the service host with its worker off, on the operator key.</summary>
public sealed class SourceClientFixture : IAsyncLifetime
{
    private WebApiTestHost<Program> host = null!;
    public TimeProvider Clock => host.Clock;
    public ServiceProvider Services { get; private set; } = null!;

    async Task IAsyncLifetime.InitializeAsync()
    {
        string connectionString = (await PostgresServer.SharedAsync()).NewDatabase("source_client_conformance");
        host = new WebApiTestHost<Program>
        {
            ConfigureConfigurationHook = configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseSettings:ConnectionString"] = connectionString,
                [HostConfiguration.OperatorApiKeySetting] = ServiceFixture.OperatorKey,
                ["Harvest:Worker:Enabled"] = "false",
                ["Logging:LogLevel:Default"] = "Warning",
            }),
        };
        var server = host.Server;
        var services = new ServiceCollection().AddLogging();
        services.AddHarvestClient(options => { options.BaseAddress = server.BaseAddress; options.ApiKey = ServiceFixture.OperatorKey; });
        services.AddHarvestRunClient();
        services.AddHarvestDataClient();
        services.AddHarvestSourceClient();
        services.ConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = server.CreateHandler()));
        Services = services.BuildServiceProvider(validateScopes: true);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await Services.DisposeAsync();
        await host.DisposeAsync();
    }
}
