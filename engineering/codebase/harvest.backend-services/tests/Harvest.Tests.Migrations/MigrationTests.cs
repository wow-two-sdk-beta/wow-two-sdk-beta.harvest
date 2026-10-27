using AwesomeAssertions;
using Harvest.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;

namespace Harvest.Tests.Migrations;

/// <summary>The module's migration set over a fresh PostgreSQL database: apply, idempotency, the Apply → Rollback → Apply
/// round trip, and the shape SQL 001 promises the lanes.</summary>
public sealed class MigrationTests : IAsyncLifetime
{
    /// <summary>Holds every table 001 creates, partitions aside.</summary>
    private static readonly string[] Tables =
    [
        "harvest_api_keys", "harvest_captures", "harvest_check_policies", "harvest_containers", "harvest_enumerations",
        "harvest_host_lane", "harvest_host_policies", "harvest_host_state", "harvest_link_check_log", "harvest_link_events",
        "harvest_link_readings", "harvest_link_schedule", "harvest_pipelines", "harvest_post_links", "harvest_post_texts",
        "harvest_posts", "harvest_retention_policies", "harvest_runs", "harvest_schedules", "harvest_source_scopes",
        "harvest_sources", "harvest_subscription_event", "harvest_subscriptions", "harvest_tenants", "harvest_usage",
        "harvest_work_item",
    ];

    private ServiceProvider services = null!;
    private string connectionString = null!;

    private IMigrationRunnerService Runner =>
        services.GetRequiredService<IMigrationSetRegistry>().GetRunner(HarvestModuleConstants.MigrationSetName);

    public async Task InitializeAsync()
    {
        connectionString = (await PostgresServer.SharedAsync()).NewDatabase("migrations");
        var collection = new ServiceCollection().AddLogging();
        collection.AddHarvest().Configure(options => options.ConnectionString = connectionString);
        collection.Configure<MigrationOptions>(HarvestModuleConstants.MigrationSetName, options => options.AllowRollback = true);
        services = collection.BuildServiceProvider();
        await services.GetRequiredKeyedService<IMigrationDialect>(HarvestModuleConstants.MigrationSetName).EnsureDatabaseExistsAsync(connectionString);
    }

    public async Task DisposeAsync()
    {
        await services.DisposeAsync();
        NpgsqlConnection.ClearAllPools();
    }

    [Fact]
    public async Task ApplyPending_ShouldCreateTheSliceSchemaAndBeIdempotent_WhenTheDatabaseIsFresh()
    {
        var applied = Unwrap(await Runner.ApplyPendingAsync("startup", CancellationToken.None));

        applied.Should().Equal("001-baseline");
        (await HarvestTablesAsync()).Should().BeEquivalentTo(Tables);
        (await ScalarAsync<long>("SELECT count(*) FROM harvest_migration_history WHERE applied_by = 'startup'")).Should().Be(1);
        Unwrap(await Runner.ApplyPendingAsync("startup", CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyPending_ShouldPartitionCapturesByMonthWithADefault_WhenTheSchemaIsCreated()
    {
        Unwrap(await Runner.ApplyPendingAsync("test", CancellationToken.None));

        (await ScalarAsync<string>("SELECT partstrat::text FROM pg_partitioned_table WHERE partrelid = 'harvest_captures'::regclass")).Should().Be("r");
        (await ScalarAsync<long>("SELECT count(*) FROM pg_inherits WHERE inhparent = 'harvest_captures'::regclass")).Should().Be(5);
        (await ScalarAsync<bool>("SELECT to_regclass('harvest_captures_default') IS NOT NULL")).Should().BeTrue();
        (await ScalarAsync<string>("SELECT 'harvest_captures_y' || to_char(now() AT TIME ZONE 'UTC', 'YYYY\"m\"MM')")).Should()
            .Match(name => name.Length == "harvest_captures_y2026m10".Length);
        (await ScalarAsync<bool>("SELECT to_regclass('harvest_captures_y' || to_char(now() AT TIME ZONE 'UTC', 'YYYY\"m\"MM')) IS NOT NULL")).Should().BeTrue();
    }

    [Fact]
    public async Task ApplyPending_ShouldInstallTrigramsAndSeedTheConfigTables_WhenTheSchemaIsCreated()
    {
        Unwrap(await Runner.ApplyPendingAsync("test", CancellationToken.None));

        (await ScalarAsync<string>("SELECT indexdef FROM pg_indexes WHERE indexname = 'ix_harvest_post_texts_match'")).Should().Contain("gin_trgm_ops");
        (await ScalarAsync<int>("SELECT array_length(show_trgm('привет'), 1)")).Should().BePositive();
        (await ScalarAsync<long>("SELECT count(*) FROM harvest_retention_policies")).Should().Be(6);
        (await ScalarAsync<string>("SELECT string_agg(key, ',' ORDER BY key) FROM harvest_check_policies")).Should().Be("none,web-direct");
    }

    [Fact]
    public async Task ApplyPending_ShouldRestoreTheSchema_WhenEveryMigrationWasRolledBack()
    {
        Unwrap(await Runner.ApplyPendingAsync("test", CancellationToken.None));

        Unwrap(await Runner.RollbackAsync(targetOrdinal: 0, CancellationToken.None)).Should().Equal("001-baseline");
        (await HarvestTablesAsync()).Should().BeEmpty();
        (await ScalarAsync<bool>("SELECT to_regproc('harvest_ensure_capture_partitions') IS NULL")).Should().BeTrue();

        Unwrap(await Runner.ApplyPendingAsync("test", CancellationToken.None)).Should().Equal("001-baseline");
        (await HarvestTablesAsync()).Should().BeEquivalentTo(Tables);
        var status = Unwrap(await Runner.GetStatusAsync(CancellationToken.None));
        status.Applied.Select(migration => migration.Ordinal).Should().Equal(1);
        status.Pending.Should().BeEmpty();
        status.Drifted.Should().BeEmpty();
    }

    private async Task<List<string>> HarvestTablesAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relkind IN ('r', 'p') AND NOT c.relispartition
              AND c.relname LIKE 'harvest\_%' AND c.relname <> 'harvest_migration_history'
            """, connection);
        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            tables.Add(reader.GetString(0));
        return tables;
    }

    private async Task<T> ScalarAsync<T>(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (T)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Returns a migrator result's value, failing the test with the migrator's own message otherwise.</summary>
    private static T Unwrap<T>(Result<T> result) where T : notnull =>
        result is Result<T>.Success success
            ? success.Value
            : throw new Xunit.Sdk.XunitException(((Result<T>.Failure)result).Error.Message);
}
