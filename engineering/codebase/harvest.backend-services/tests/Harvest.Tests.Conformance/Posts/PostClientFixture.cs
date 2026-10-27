using Harvest.Api.Configurations;
using Harvest.Tests.Conformance.Registration;
using Harvest.Tests.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Http;
using WoW.Two.Sdk.Beta.Be.Harvest.Client;
using WoW.Two.Sdk.Beta.Be.Harvest.Client.Delivery;
using WoW.Two.Sdk.Beta.Be.Testing;

namespace Harvest.Tests.Conformance.Posts;

public sealed class PostClientFixture : IAsyncLifetime
{
    private WebApiTestHost<Program>? host;
    public TimeProvider Clock => host?.Clock ?? TimeProvider.System;
    public ServiceProvider Services { get; private set; } = null!;
    async Task IAsyncLifetime.InitializeAsync()
    {
        var services = new ServiceCollection().AddLogging();
        var runningUrl = Environment.GetEnvironmentVariable(ServiceFixture.BaseUrlVariable);
        if (!string.IsNullOrWhiteSpace(runningUrl))
        {
            services.AddHarvestClient(options =>
            {
                options.BaseAddress = new Uri(runningUrl); options.ApiKey = Environment.GetEnvironmentVariable(ServiceFixture.ApiKeyVariable);
            });
        }
        else
        {
            var connectionString = (await PostgresServer.SharedAsync()).NewDatabase("post_client_conformance");
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
            services.AddHarvestClient(options => { options.BaseAddress = server.BaseAddress; options.ApiKey = ServiceFixture.OperatorKey; });
            services.ConfigureAll<HttpClientFactoryOptions>(options => options.HttpMessageHandlerBuilderActions.Add(builder => builder.PrimaryHandler = server.CreateHandler()));
        }
        services.AddHarvestDataClient();
        Services = services.BuildServiceProvider(validateScopes: true);
    }
    async Task IAsyncLifetime.DisposeAsync()
    {
        await Services.DisposeAsync();
        if (host is not null) await host.DisposeAsync();
    }
}
