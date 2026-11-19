using Harvest.Api.Configurations;
using Harvest.Tests.Conformance.Registration;
using Harvest.Tests.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using WoW.Two.Sdk.Beta.Be.Harvest.Client;
using WoW.Two.Sdk.Beta.Be.Harvest.Client.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Client.Entities;
using WoW.Two.Sdk.Beta.Be.Testing;

namespace Harvest.Tests.Conformance.Entities;

/// <summary>Forms 2 and 3: the client against the service host in process with the operator key, its worker disabled;
/// entities are seeded straight into the host's database, so this form never targets a running image.</summary>
public sealed class EntityClientFixture : IAsyncLifetime
{
    private WebApiTestHost<Program> host = null!;
    public TimeProvider Clock => host.Clock;
    public ServiceProvider Services { get; private set; } = null!;
    public string ConnectionString { get; private set; } = null!;

    async Task IAsyncLifetime.InitializeAsync()
    {
        ConnectionString = (await PostgresServer.SharedAsync()).NewDatabase("entity_client_conformance");
        host = new WebApiTestHost<Program>
        {
            ConfigureConfigurationHook = configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["DatabaseSettings:ConnectionString"] = ConnectionString,
                [HostConfiguration.OperatorApiKeySetting] = ServiceFixture.OperatorKey,
                ["Harvest:Worker:Enabled"] = "false",
                ["Logging:LogLevel:Default"] = "Warning",
            }),
        };
        var server = host.Server;
        var services = new ServiceCollection().AddLogging();
        services.AddHarvestClient(options => { options.BaseAddress = server.BaseAddress; options.ApiKey = ServiceFixture.OperatorKey; });
        services.AddHarvestDataClient().AddHarvestEntityClient();
        services.ConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = server.CreateHandler()));
        Services = services.BuildServiceProvider(validateScopes: true);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await Services.DisposeAsync();
        await host.DisposeAsync();
    }
}
