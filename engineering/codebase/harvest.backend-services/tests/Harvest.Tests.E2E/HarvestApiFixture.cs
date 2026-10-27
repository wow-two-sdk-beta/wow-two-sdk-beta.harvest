using System.Net.Http.Headers;
using Harvest.Api.Configurations;
using Harvest.Tests.Shared;
using Microsoft.Extensions.Configuration;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;
using WoW.Two.Sdk.Beta.Be.Testing;

namespace Harvest.Tests.E2E;

/// <summary>Boots the real API — its store boot, self-test and migrations — over a fresh PostgreSQL database.</summary>
public sealed class HarvestApiFixture : IAsyncLifetime
{
    /// <summary>Holds the public host the test target allows, beside <c>localhost</c>, as a deployed target's <c>AllowedHosts</c> does.</summary>
    public const string PublicHost = "harvest.example.test";

    /// <summary>Holds the operator key the host is configured with — a test value, never a deployed one.</summary>
    public const string OperatorKey = "hv_EndToEndOperatorKey0000000000000";

    /// <summary>Gets the booted API host.</summary>
    public WebApiTestHost<Program> Host { get; private set; } = null!;

    /// <summary>Creates a client that sends <paramref name="apiKey"/>, or no key when null.</summary>
    /// <param name="apiKey">The key to send in <c>X-Api-Key</c>.</param>
    public HttpClient CreateClient(string? apiKey = null)
    {
        var client = Host.CreateClient();
        if (apiKey is not null)
            client.DefaultRequestHeaders.Add(ApiKeyAuthenticationDefaults.HeaderName, apiKey);
        client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
        return client;
    }

    /// <summary>Creates a host over <paramref name="connectionString"/> with the test target's settings; it boots on the
    /// first client.</summary>
    /// <param name="connectionString">The database.</param>
    public static WebApiTestHost<Program> CreateHost(string connectionString) => new()
    {
        ConfigureConfigurationHook = configuration => configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["DatabaseSettings:ConnectionString"] = connectionString,
            [HostConfiguration.OperatorApiKeySetting] = OperatorKey,
            ["AllowedHosts"] = PublicHost + ";localhost",
            ["Logging:LogLevel:Default"] = "Warning",
            ["Harvest:Worker:Enabled"] = "false",
        }),
    };

    async Task IAsyncLifetime.InitializeAsync()
    {
        Host = CreateHost((await PostgresServer.SharedAsync()).NewDatabase("e2e"));

        // Building a client starts the host, which creates the database, checks it and applies the migration set.
        using var client = Host.CreateClient();
    }

    async Task IAsyncLifetime.DisposeAsync()
    {
        await Host.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
    }
}

/// <summary>Shares one <see cref="HarvestApiFixture"/> across the whole E2E run.</summary>
[CollectionDefinition(Name)]
public sealed class HarvestApiCollection : ICollectionFixture<HarvestApiFixture>
{
    /// <summary>The collection name every E2E class joins.</summary>
    public const string Name = "harvest-api";
}
