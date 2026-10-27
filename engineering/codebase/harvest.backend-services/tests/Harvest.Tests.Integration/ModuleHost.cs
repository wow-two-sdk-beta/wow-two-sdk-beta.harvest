using Harvest.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Time.Testing;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;

namespace Harvest.Tests.Integration;

/// <summary>The module wired as a Form 1 consumer wires it, over a fresh database on the tier's PostgreSQL server.</summary>
public sealed class ModuleHost : IAsyncDisposable
{
    private ModuleHost(ServiceProvider services, string connectionString, FakeTimeProvider clock)
    {
        Services = services;
        ConnectionString = connectionString;
        Clock = clock;
    }

    /// <summary>Gets the consumer's container.</summary>
    public ServiceProvider Services { get; }

    /// <summary>Gets the database's connection string.</summary>
    public string ConnectionString { get; }

    /// <summary>Gets the clock the module stamps rows with.</summary>
    public FakeTimeProvider Clock { get; }

    /// <summary>Builds the host over <paramref name="connectionString"/>, or a fresh database when null; boots nothing.</summary>
    /// <param name="connectionString">The database, or null for a fresh one.</param>
    /// <param name="operatorKey">The operator key the module accepts.</param>
    /// <param name="configure">A further tweak of the module's options, applied after the connection and key.</param>
    public static async Task<ModuleHost> CreateAsync(string? connectionString = null, string? operatorKey = null, Action<HarvestModuleOptions>? configure = null)
    {
        connectionString ??= (await PostgresServer.SharedAsync()).NewDatabase("integration");
        var clock = new FakeTimeProvider(new DateTimeOffset(2026, 10, 27, 9, 0, 0, TimeSpan.Zero));
        var services = new ServiceCollection().AddLogging().AddSingleton<TimeProvider>(clock);
        services.AddHarvest().Configure(options =>
        {
            options.ConnectionString = connectionString;
            options.OperatorApiKey = operatorKey;
            configure?.Invoke(options);
        });
        services.AddHarvestApiKeys();
        return new ModuleHost(services.BuildServiceProvider(validateScopes: true), connectionString, clock);
    }

    /// <summary>Builds the host over a fresh database and boots its store.</summary>
    /// <param name="operatorKey">The operator key the module accepts.</param>
    /// <param name="configure">A further tweak of the module's options, applied after the connection and key.</param>
    public static async Task<ModuleHost> StartAsync(string? operatorKey = null, Action<HarvestModuleOptions>? configure = null)
    {
        var host = await CreateAsync(operatorKey: operatorKey, configure: configure);
        await host.Services.MigrateHarvestOnStartupAsync("test");
        return host;
    }

    /// <summary>Runs a scalar query on the database.</summary>
    /// <typeparam name="T">The scalar's type.</typeparam>
    /// <param name="sql">The query.</param>
    public async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        await Services.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
    }
}
