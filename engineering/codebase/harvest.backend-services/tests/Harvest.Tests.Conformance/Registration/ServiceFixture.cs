using Harvest.Api.Configurations;
using Harvest.Tests.Shared;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Harvest.Client;
using WoW.Two.Sdk.Beta.Be.Testing;

namespace Harvest.Tests.Conformance.Registration;

/// <summary>Forms 2 and 3: the client wired as a consumer wires it, against the service host with the operator key.</summary>
/// <remarks>By default the host runs in process (WebApplicationFactory) over a fresh PostgreSQL database. With
/// <c>CONFORMANCE_BASE_URL</c> and <c>CONFORMANCE_API_KEY</c> set, the client calls that running host or image instead, on
/// the real clock.</remarks>
public sealed class ServiceFixture : IAsyncLifetime
{
    /// <summary>Holds the variable that points the suite at a running host or image.</summary>
    public const string BaseUrlVariable = "CONFORMANCE_BASE_URL";

    /// <summary>Holds the variable that carries the running host's operator key.</summary>
    public const string ApiKeyVariable = "CONFORMANCE_API_KEY";

    /// <summary>Holds the operator key the in-process host is configured with — a test value, never a deployed one.</summary>
    public const string OperatorKey = "hv_ConformanceOperatorKey0000000000";

    private readonly Uri? runningHost = Environment.GetEnvironmentVariable(BaseUrlVariable) is { Length: > 0 } url ? new Uri(url) : null;
    private WebApiTestHost<Program>? host;

    /// <summary>Gets the clock the service stamps rows with.</summary>
    public TimeProvider Clock => host?.Clock ?? TimeProvider.System;

    /// <summary>Gets the consumer's container.</summary>
    public ServiceProvider Services { get; private set; } = null!;

    async Task IAsyncLifetime.InitializeAsync()
    {
        var services = new ServiceCollection().AddLogging();
        if (runningHost is not null)
        {
            var key = Environment.GetEnvironmentVariable(ApiKeyVariable);
            services.AddHarvestClient(options =>
            {
                options.BaseAddress = runningHost;
                options.ApiKey = key;
            });
        }
        else
        {
            var connectionString = (await PostgresServer.SharedAsync()).NewDatabase("conformance_service");
            host = new WebApiTestHost<Program>
            {
                ConfigureConfigurationHook = configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["DatabaseSettings:ConnectionString"] = connectionString,
                    [HostConfiguration.OperatorApiKeySetting] = OperatorKey,
                    ["Logging:LogLevel:Default"] = "Warning",
                }),
            };
            var testServer = host.Server;
            services.AddHarvestClient(options =>
                {
                    options.BaseAddress = testServer.BaseAddress;
                    options.ApiKey = OperatorKey;
                })
                .ConfigurePrimaryHttpMessageHandler(testServer.CreateHandler);
        }

        Services = services.BuildServiceProvider(validateScopes: true);
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await Services.DisposeAsync();
        if (host is not null)
            await host.DisposeAsync();
    }
}
