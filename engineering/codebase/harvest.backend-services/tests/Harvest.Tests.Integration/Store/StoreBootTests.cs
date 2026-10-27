using AwesomeAssertions;
using Harvest.Tests.Shared;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;

namespace Harvest.Tests.Integration.Store;

/// <summary>The store's boot: create, check, migrate, check — and the refusal of a database Harvest cannot use.</summary>
public sealed class StoreBootTests
{
    [Fact]
    public async Task MigrateHarvestOnStartup_ShouldCreateTheDatabaseAndApplyTheSetOnce_WhenTheDatabaseIsMissing()
    {
        await using var host = await ModuleHost.CreateAsync();

        var first = await host.Services.MigrateHarvestOnStartupAsync("test");
        var second = await host.Services.MigrateHarvestOnStartupAsync("test");

        first.Should().Equal("001-baseline");
        second.Should().BeEmpty();
        (await host.ScalarAsync<long>("SELECT count(*) FROM harvest_migration_history")).Should().Be(1);
        (await host.ScalarAsync<bool>("SELECT EXISTS (SELECT 1 FROM pg_extension WHERE extname = 'pg_trgm')")).Should().BeTrue();
    }

    [Fact]
    public async Task MigrateHarvestOnStartup_ShouldKeepItsHistoryApart_WhenTheConsumerHasItsOwnTables()
    {
        await using var host = await ModuleHost.CreateAsync();
        await host.Services.MigrateHarvestOnStartupAsync("test");

        (await host.ScalarAsync<bool>("SELECT to_regclass('public.migration_history') IS NULL")).Should().BeTrue();
        (await host.ScalarAsync<long>("SELECT count(*) FROM pg_tables WHERE schemaname = 'public' AND tablename NOT LIKE 'harvest\\_%'"))
            .Should().Be(0, "the module adds only harvest_-prefixed tables beside a consumer's own");
    }

    [Fact]
    public async Task MigrateHarvestOnStartup_ShouldRefuseAndApplyNothing_WhenTheDatabaseIsNotUtf8()
    {
        var connectionString = await (await PostgresServer.SharedAsync()).CreateDatabaseAsync("ascii", "SQL_ASCII");
        await using var host = await ModuleHost.CreateAsync(connectionString);

        var boot = () => host.Services.MigrateHarvestOnStartupAsync("test");

        (await boot.Should().ThrowAsync<InvalidOperationException>())
            .WithMessage("Harvest refuses to start on this database: the database encoding is SQL_ASCII, not UTF8*");
        (await host.ScalarAsync<bool>("SELECT to_regclass('public.harvest_tenants') IS NULL")).Should().BeTrue();
    }

    [Fact]
    public async Task EnsureCapturePartitions_ShouldCreateOnlyTheMissingMonths_WhenRunAgainWithMoreMonths()
    {
        await using var host = await ModuleHost.StartAsync();

        var created = await host.ScalarAsync<int>("SELECT harvest_ensure_capture_partitions(5)");
        var again = await host.ScalarAsync<int>("SELECT harvest_ensure_capture_partitions(5)");

        created.Should().Be(2, "001 already made the current month and three ahead");
        again.Should().Be(0);
        (await host.ScalarAsync<long>("SELECT count(*) FROM pg_inherits WHERE inhparent = 'harvest_captures'::regclass")).Should().Be(7);
    }

    [Fact]
    public async Task Captures_ShouldLandInTheMonthsPartition_WhenTheMonthExists()
    {
        await using var host = await ModuleHost.StartAsync();

        var partition = await host.ScalarAsync<string>(
            """
            WITH tenant AS (INSERT INTO harvest_containers (id, kind, created_at) VALUES (gen_random_uuid(), 'pool', now()) RETURNING id),
                 source AS (INSERT INTO harvest_sources (id, container_id, family, handle, coverage, completeness, posture, state_since, created_at)
                            SELECT gen_random_uuid(), id, 'site', 'mstar.uz', 'whole', 'complete', 'own_fetch', now(), now() FROM tenant RETURNING id, container_id),
                 capture AS (INSERT INTO harvest_captures (id, container_id, source_id, captured_at, request_url, final_url, posture, raw_hash, content_hash, retention_class)
                             SELECT gen_random_uuid(), container_id, id, now(), 'https://mstar.uz/e/1', 'https://mstar.uz/e/1', 'own_fetch',
                                    sha256('raw'), sha256('content'), 'standard' FROM source RETURNING tableoid)
            SELECT tableoid::regclass::text FROM capture
            """);

        partition.Should().MatchRegex("^harvest_captures_y\\d{4}m\\d{2}$");
    }
}
