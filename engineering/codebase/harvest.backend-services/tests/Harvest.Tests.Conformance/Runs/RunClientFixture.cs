using Harvest.Api.Configurations;
using Harvest.Tests.Conformance.Registration;
using Harvest.Tests.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using WoW.Two.Sdk.Beta.Be.Harvest.Client;
using WoW.Two.Sdk.Beta.Be.Harvest.Client.Runs;
using WoW.Two.Sdk.Beta.Be.Testing;

namespace Harvest.Tests.Conformance.Runs;

/// <summary>Uses the actual service routes in-process with its worker disabled, so queue controls never fetch sources.</summary>
public sealed class RunClientFixture : IAsyncLifetime
{
    private WebApiTestHost<Program> host = null!;
    public ServiceProvider Services { get; private set; } = null!;

    async Task IAsyncLifetime.InitializeAsync()
    {
        string connectionString = (await PostgresServer.SharedAsync()).NewDatabase("run_client_conformance");
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
        services.ConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = server.CreateHandler()));
        Services = services.BuildServiceProvider(validateScopes: true);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await Services.DisposeAsync();
        await host.DisposeAsync();
    }
}
