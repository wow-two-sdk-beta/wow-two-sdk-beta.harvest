using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Store;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module;

/// <summary>Brings the module's store up at boot: creates the database, checks it, applies the module's migration set,
/// and checks again.</summary>
public static class HarvestStoreStartupExtensions
{
    /// <summary>Creates the database if missing, refuses a database that is not UTF-8 or lacks <c>pg_trgm</c>, applies the
    /// <see cref="HarvestModuleConstants.MigrationSetName"/> set stamped <paramref name="appliedBy"/>, then refuses a database
    /// whose trigrams skip Cyrillic. Idempotent: safe on every boot.</summary>
    /// <param name="services">The root service provider of a host that called <c>AddHarvest</c>.</param>
    /// <param name="appliedBy">The stamp on applied history rows, such as <c>startup</c> or <c>deploy</c>.</param>
    /// <param name="cancellationToken">The token that cancels the boot.</param>
    /// <returns>The labels of the migrations applied, empty when the store was current.</returns>
    /// <exception cref="InvalidOperationException">The database cannot hold Harvest; the message names what is missing.</exception>
    public static async Task<IReadOnlyList<string>> MigrateHarvestOnStartupAsync(
        this IServiceProvider services, string appliedBy = "startup", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);

        var connectionString = services.GetRequiredService<IOptions<HarvestModuleOptions>>().Value.ConnectionString;
        var dialect = services.GetRequiredKeyedService<IMigrationDialect>(HarvestModuleConstants.MigrationSetName);
        await dialect.EnsureDatabaseExistsAsync(connectionString, cancellationToken);

        var connections = services.GetRequiredKeyedService<IDbConnectionFactory>(HarvestModuleConstants.ServiceKey);
        StoreProbe probe;
        await using (var connection = await connections.CreateOpenAsync(cancellationToken))
            probe = await StoreSelfTest.ProbeBeforeMigrationAsync(connection, cancellationToken);
        ThrowIfRefused(probe);

        var runner = services.GetRequiredService<IMigrationSetRegistry>().GetRunner(HarvestModuleConstants.MigrationSetName);
        var applied = (await runner.ApplyPendingAsync(appliedBy, cancellationToken)).ValueOrThrow();

        await using (var connection = await connections.CreateOpenAsync(cancellationToken))
            probe = await StoreSelfTest.ProbeAfterMigrationAsync(connection, probe, cancellationToken);
        ThrowIfRefused(probe);

        return applied;
    }

    private static void ThrowIfRefused(StoreProbe probe)
    {
        var failures = StoreSelfTest.Evaluate(probe);
        if (failures.Count > 0)
            throw new InvalidOperationException(StoreSelfTest.Describe(failures));
    }
}
