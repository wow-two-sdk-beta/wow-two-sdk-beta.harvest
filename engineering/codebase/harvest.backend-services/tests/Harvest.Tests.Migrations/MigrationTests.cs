using AwesomeAssertions;
using Harvest.Tests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Testing.Data.Migrations.Lint;

namespace Harvest.Tests.Migrations;

/// <summary>The module's migration set over a fresh PostgreSQL database: apply, idempotency, the Apply → Rollback → Apply
/// round trip, the be sdk lint from 011 on, and the shape SQL 001, 002, 006, 011, 012, 013, 014, 015, 016, 017, 018, 019, 020 and 021 promise the lanes.</summary>
public sealed class MigrationTests : IAsyncLifetime
{
    /// <summary>Holds every migration of the set, in order.</summary>
    private static readonly string[] Migrations =
        ["001-baseline", "002-drop-escalation", "003-fetching", "004-posts-delivery", "005-subscription-filter", "006-source-groups",
            "007-source-assets", "008-pipeline-stages", "009-step-operations", "010-post-unmatched", "011-taxonomy", "012-post-tags",
            "013-post-extractions", "014-entities", "015-entity-events", "016-model-processing", "017-processing-reruns", "018-subscription-selectors", "019-source-catalog", "020-asset-outcomes", "021-pipeline-auto-off"];

    /// <summary>Holds the escalation columns 002 drops.</summary>
    private const string EscalationColumns =
        "'rung', 'rung_since', 'probe_due_at', 'cost_per_success_usd', 'egress', 'egress_lease_id', 'session_lease_id'";

    /// <summary>Holds every table the set creates, partitions aside; 002 drops none.</summary>
    private static readonly string[] Tables =
    [
        "harvest_api_keys", "harvest_captures", "harvest_check_policies", "harvest_containers", "harvest_enumerations",
        "harvest_fetch_validators", "harvest_host_lane", "harvest_host_policies", "harvest_host_state", "harvest_link_check_log", "harvest_link_events",
        "harvest_link_readings", "harvest_link_schedule", "harvest_pipelines", "harvest_post_links", "harvest_post_texts",
        "harvest_posts", "harvest_post_merges", "harvest_post_keep_apart", "harvest_post_revisions", "harvest_retention_policies", "harvest_runs",
        "harvest_schedules", "harvest_source_assets", "harvest_source_groups", "harvest_source_scopes",
        "harvest_sources", "harvest_subscription_event", "harvest_subscriptions", "harvest_tenants", "harvest_usage",
        "harvest_work_item", "harvest_discoveries", "harvest_discovery_scopes", "harvest_catalog_pages",
        "harvest_processing_results", "harvest_step_attempts", "harvest_taxonomy_nodes", "harvest_post_tags", "harvest_post_work",
        "harvest_post_extractions", "harvest_entities", "harvest_entity_occurrences", "harvest_entity_relations",
        "harvest_entity_keep_apart", "harvest_entity_flags", "harvest_entity_events", "harvest_processing_runs", "harvest_subscription_selectors", "harvest_catalog_sources",
    ];

    /// <summary>The last ordinal applied before the lint ran in this tier.</summary>
    private const int LintCutoff = 10;

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

        applied.Should().Equal(Migrations);
        (await HarvestTablesAsync()).Should().BeEquivalentTo(Tables);
        (await ScalarAsync<long>("SELECT count(*) FROM harvest_migration_history WHERE applied_by = 'startup'")).Should().Be(Migrations.Length);
        Unwrap(await Runner.ApplyPendingAsync("startup", CancellationToken.None)).Should().BeEmpty();
    }

    [Fact]
    public async Task ApplyPending_ShouldDropTheEscalationColumnsAndKeepTheLaneBackoff_WhenTheLadderIsGone()
    {
        Unwrap(await Runner.ApplyPendingAsync("test", CancellationToken.None));

        (await ColumnCountAsync($"column_name IN ({EscalationColumns})")).Should().Be(0);
        (await ColumnCountAsync("table_name = 'harvest_host_lane' AND column_name IN ('paused_until', 'consecutive_failures', "
            + "'last_challenge_kind', 'max_concurrency', 'in_flight')")).Should().Be(5);
        (await ConstraintAsync("ck_harvest_usage_meter")).Should().NotContain("solves").And.NotContain("egress").And.Contain("render_seconds");
        (await ConstraintAsync("ck_harvest_work_item_route")).Should().NotContain("account").And.Contain("render");
        (await ConstraintAsync("ck_harvest_pipelines_pause")).Should().NotContain("account").And.Contain("operator");
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

        Unwrap(await Runner.RollbackAsync(targetOrdinal: 5, CancellationToken.None)).Should()
            .Equal("021-pipeline-auto-off", "020-asset-outcomes", "019-source-catalog", "018-subscription-selectors", "017-processing-reruns", "016-model-processing", "015-entity-events", "014-entities", "013-post-extractions", "012-post-tags", "011-taxonomy", "010-post-unmatched", "009-step-operations",
                "008-pipeline-stages", "007-source-assets", "006-source-groups");
        (await ColumnCountAsync("table_name = 'harvest_sources' AND column_name IN ('group_id', 'display_name')")).Should().Be(0);
        (await ScalarAsync<bool>("SELECT to_regclass('harvest_source_groups') IS NULL AND to_regclass('harvest_post_revisions') IS NULL")).Should().BeTrue();

        Unwrap(await Runner.RollbackAsync(targetOrdinal: 1, CancellationToken.None)).Should().Equal("005-subscription-filter", "004-posts-delivery", "003-fetching", "002-drop-escalation");
        (await ColumnCountAsync($"column_name IN ({EscalationColumns})")).Should().Be(11, "002's rollback restores every column 001 made");
        (await ConstraintAsync("ck_harvest_work_item_route")).Should().Contain("account");

        Unwrap(await Runner.RollbackAsync(targetOrdinal: 0, CancellationToken.None)).Should().Equal("001-baseline");
        (await HarvestTablesAsync()).Should().BeEmpty();
        (await ScalarAsync<bool>("SELECT to_regclass('harvest_migration_history_events') IS NOT NULL"))
            .Should().BeTrue("the SDK keeps its append-only migration audit after a rollback");
        (await ScalarAsync<bool>("SELECT to_regproc('harvest_ensure_capture_partitions') IS NULL")).Should().BeTrue();

        Unwrap(await Runner.ApplyPendingAsync("test", CancellationToken.None)).Should().Equal(Migrations);
        (await HarvestTablesAsync()).Should().BeEquivalentTo(Tables);
        var status = Unwrap(await Runner.GetStatusAsync(CancellationToken.None));
        status.Applied.Select(migration => migration.Ordinal).Should().Equal(Enumerable.Range(1, Migrations.Length));
        status.Pending.Should().BeEmpty();
        status.Drifted.Should().BeEmpty();
    }

    [Fact]
    public async Task Rollback_ShouldDropStagedDiscovery_WhichOlderCodeWouldRunAsLegacyDiscovery()
    {
        Unwrap(await Runner.ApplyPendingAsync("test", CancellationToken.None));
        Guid pool = Guid.NewGuid(), source = Guid.NewGuid(), staged = Guid.NewGuid(), legacy = Guid.NewGuid();
        Guid pending = Guid.NewGuid(), finished = Guid.NewGuid(), queued = Guid.NewGuid();
        await ExecuteAsync($$"""
            INSERT INTO harvest_containers(id,kind,created_at) VALUES('{{pool}}','pool',now());
            INSERT INTO harvest_sources(id,container_id,family,handle,coverage,completeness,posture,state_since,created_at)
            VALUES('{{source}}','{{pool}}','site','www.afisha.uz','whole','complete','own_fetch',now(),now());
            INSERT INTO harvest_pipelines(id,container_id,source_id,scope_key,state,state_since,created_at)
            VALUES('{{staged}}','{{pool}}','{{source}}','staged','active',now(),now()),
                  ('{{legacy}}','{{pool}}','{{source}}','legacy','active',now(),now());
            INSERT INTO harvest_schedules(id,container_id,pipeline_id,kind,mode,created_at,stage_options)
            VALUES(gen_random_uuid(),'{{pool}}','{{staged}}','discover','manual',now(),'{"batchSize":5}'),
                  (gen_random_uuid(),'{{pool}}','{{legacy}}','discover','manual',now(),NULL);
            INSERT INTO harvest_runs(id,container_id,pipeline_id,kind,trigger,scheduled_for,queued_at,status,frozen)
            VALUES('{{pending}}','{{pool}}','{{staged}}','discover','manual',now(),now(),'queued',jsonb_build_object('stage', jsonb_build_object('batchSize', 5))),
                  ('{{finished}}','{{pool}}','{{staged}}','discover','manual',now()-interval '1 day',now(),'completed',jsonb_build_object('stage', jsonb_build_object('batchSize', 5))),
                  ('{{queued}}','{{pool}}','{{legacy}}','discover','manual',now(),now(),'queued','{}');
            """);

        Unwrap(await Runner.RollbackAsync(targetOrdinal: 7, CancellationToken.None)).Should()
            .Equal("021-pipeline-auto-off", "020-asset-outcomes", "019-source-catalog", "018-subscription-selectors", "017-processing-reruns", "016-model-processing", "015-entity-events", "014-entities", "013-post-extractions", "012-post-tags", "011-taxonomy", "010-post-unmatched", "009-step-operations",
                "008-pipeline-stages");

        (await ScalarAsync<string>("SELECT string_agg(pipeline_id::text, ',') FROM harvest_schedules WHERE kind = 'discover'"))
            .Should().Be(legacy.ToString());
        (await ScalarAsync<string>("SELECT string_agg(id::text, ',' ORDER BY queued_at, id) FROM harvest_runs WHERE kind = 'discover'"))
            .Split(',').Should().BeEquivalentTo(finished.ToString(), queued.ToString());
    }

    [Fact]
    public async Task ApplyPending_ShouldKeepGroupsInTheirContainerAndUniqueWhateverTheCase_WhenSourceGroupsExist()
    {
        Unwrap(await Runner.ApplyPendingAsync("test", CancellationToken.None));
        Guid pool = Guid.NewGuid(), other = Guid.NewGuid(), group = Guid.NewGuid(), source = Guid.NewGuid();
        await ExecuteAsync($"""
            INSERT INTO harvest_tenants(id,slug,name,created_at) VALUES('{other}','other','Other',now());
            INSERT INTO harvest_containers(id,kind,created_at) VALUES('{pool}','pool',now());
            INSERT INTO harvest_containers(id,kind,tenant_id,created_at) VALUES('{other}','private','{other}',now());
            INSERT INTO harvest_source_groups(id,container_id,name,created_at) VALUES('{group}','{pool}','Afisha',now());
            INSERT INTO harvest_sources(id,container_id,family,handle,coverage,completeness,posture,state_since,created_at,group_id,display_name)
            VALUES('{source}','{pool}','site','www.afisha.uz','whole','complete','own_fetch',now(),now(),'{group}','Afisha.uz');
            """);

        (await ScalarAsync<string>($"SELECT display_name FROM harvest_sources WHERE id='{source}'")).Should().Be("Afisha.uz");
        await FailsAsync($"INSERT INTO harvest_source_groups(id,container_id,name,created_at) VALUES(gen_random_uuid(),'{pool}','AFISHA',now())",
            "ux_harvest_source_groups_name");
        await FailsAsync($"""
            INSERT INTO harvest_sources(id,container_id,family,handle,coverage,completeness,posture,state_since,created_at,group_id)
            VALUES(gen_random_uuid(),'{other}','site','voyage.uz','whole','complete','own_fetch',now(),now(),'{group}')
            """, "fk_harvest_sources_group");
        await FailsAsync($"UPDATE harvest_sources SET display_name='' WHERE id='{source}'", "ck_harvest_sources_display_name");
        (await ScalarAsync<string>("SELECT indexdef FROM pg_indexes WHERE indexname = 'pk_harvest_post_revisions'")).Should().Contain("(post_id, revision)");
    }

    [Fact]
    public async Task Lint_ShouldFindNothing_InFilesAfterTheLintCutoff()
    {
        // 001-010 were applied everywhere before the rule; every later file expands and keeps existing tables lock-safe.
        await MigrationLint.AssertCleanAsync(services, lint => lint
            .ExpandOnlyAfter(HarvestModuleConstants.MigrationSetName, LintCutoff)
            .LintAfter(HarvestModuleConstants.MigrationSetName, LintCutoff));
    }

    [Fact]
    public async Task ApplyPending_ShouldSeedTheIabDomainsAndKeepTagsPerPostAndContent_WhenPostTagsArrive()
    {
        Unwrap(await Runner.ApplyPendingAsync("test", CancellationToken.None));

        (await ScalarAsync<long>("SELECT count(*) FROM harvest_taxonomy_nodes WHERE tier = 1 AND active")).Should().Be(34);
        (await ScalarAsync<long>("SELECT count(*) FROM harvest_taxonomy_nodes")).Should().Be(657);
        (await ScalarAsync<long>("SELECT count(*) FROM harvest_taxonomy_nodes WHERE key IN ('events', 'genres', 'sensitive-topics')")).Should().Be(0);
        (await ScalarAsync<string>("SELECT label FROM harvest_taxonomy_nodes WHERE key = 'style-fashion/womens-fashion'")).Should().Be("Women's Fashion");
        await FailsAsync("INSERT INTO harvest_taxonomy_nodes(key, parent_key, tier, label, extension) VALUES('textiles', 'style-fashion', 2, 'Textiles', true)",
            "ck_harvest_taxonomy_nodes_parent");
        await ExecuteAsync("INSERT INTO harvest_taxonomy_nodes(key, parent_key, tier, label, extension) VALUES('style-fashion/textiles', 'style-fashion', 2, 'Textiles', true)");
        (await ScalarAsync<string>("SELECT indexdef FROM pg_indexes WHERE indexname = 'pk_harvest_post_tags'")).Should().Contain("(post_id, content_hash)");
        (await ConstraintAsync("ck_harvest_post_tags_entity_type")).Should().Contain("unsure");
        (await ConstraintAsync("ck_harvest_post_work_kind")).Should().Contain("post-tags");
    }

    [Fact]
    public async Task ApplyPending_ShouldKeepOneExtractionPerPostContentAndUseCase_AndQueueItBesideTags()
    {
        Unwrap(await Runner.ApplyPendingAsync("test", CancellationToken.None));

        (await ScalarAsync<string>("SELECT indexdef FROM pg_indexes WHERE indexname = 'pk_harvest_post_extractions'"))
            .Should().Contain("(post_id, content_hash, use_case)");
        (await ConstraintAsync("ck_harvest_post_extractions_use_case")).Should().Contain("extract-event").And.Contain("extract-listing");
        (await ConstraintAsync("ck_harvest_post_extractions_role")).Should().Contain("announcement").And.Contain("unsure");
        (await ConstraintAsync("ck_harvest_post_work_kind")).Should().Contain("post-tags").And.Contain("extract-event").And.Contain("extract-listing");
        (await ScalarAsync<bool>("SELECT convalidated FROM pg_constraint WHERE conname = 'ck_harvest_post_work_kind'")).Should().BeTrue();

        Unwrap(await Runner.RollbackAsync(targetOrdinal: 12, CancellationToken.None)).Should().Equal("021-pipeline-auto-off", "020-asset-outcomes", "019-source-catalog", "018-subscription-selectors", "017-processing-reruns", "016-model-processing",
            "015-entity-events", "014-entities", "013-post-extractions");
        (await ScalarAsync<bool>("SELECT to_regclass('harvest_post_extractions') IS NULL")).Should().BeTrue();
        (await ConstraintAsync("ck_harvest_post_work_kind")).Should().NotContain("extract-event");
    }

    [Fact]
    public async Task ApplyPending_ShouldKeepEntitiesWithOccurrencesRelationsAndFlags_AndQueueResolutionBesideExtraction()
    {
        Unwrap(await Runner.ApplyPendingAsync("test", CancellationToken.None));

        (await ScalarAsync<string>("SELECT indexdef FROM pg_indexes WHERE indexname = 'pk_harvest_entity_relations'"))
            .Should().Contain("(post_id, entity_id)");
        (await ScalarAsync<string>("SELECT indexdef FROM pg_indexes WHERE indexname = 'ix_harvest_entities_name_key'")).Should().Contain("gin_trgm_ops");
        (await ConstraintAsync("ck_harvest_entities_type")).Should().Contain("event").And.Contain("listing");
        (await ConstraintAsync("ck_harvest_entities_cadence")).Should().Contain("semiannual").And.Contain("irregular");
        (await ConstraintAsync("ck_harvest_entity_occurrences_certainty")).Should().Contain("stated").And.Contain("estimated");
        (await ConstraintAsync("ck_harvest_entity_relations_role")).Should().Contain("mention").And.Contain("unsure");
        (await ConstraintAsync("ck_harvest_post_work_kind")).Should().Contain("extract-listing").And.Contain("entity-resolve");
        (await ScalarAsync<bool>("SELECT convalidated FROM pg_constraint WHERE conname = 'ck_harvest_post_work_kind'")).Should().BeTrue();
        Guid pool = Guid.NewGuid(), entity = Guid.NewGuid();
        await ExecuteAsync($"""
            INSERT INTO harvest_containers(id,kind,created_at) VALUES('{pool}','pool',now());
            INSERT INTO harvest_entities(id,container_id,entity_type,name,name_key,first_seen_at,last_seen_at,created_at,updated_at)
            VALUES('{entity}','{pool}','event','Tashkent Fashion Week','fashion tashkent week',now(),now(),now(),now());
            """);
        await FailsAsync($"""
            INSERT INTO harvest_entity_occurrences(id,entity_id,label,starts,precision,certainty,predicted,created_at,updated_at)
            VALUES(gen_random_uuid(),'{entity}','2027','2027-10-18','day','stated',true,now(),now())
            """, "ck_harvest_entity_occurrences_predicted");
        await FailsAsync($"""
            INSERT INTO harvest_entity_keep_apart(container_id,left_entity_id,right_entity_id,created_at) VALUES('{pool}','{entity}','{entity}',now())
            """, "ck_harvest_entity_keep_apart_order");

        Unwrap(await Runner.RollbackAsync(targetOrdinal: 13, CancellationToken.None)).Should().Equal("021-pipeline-auto-off", "020-asset-outcomes", "019-source-catalog", "018-subscription-selectors", "017-processing-reruns", "016-model-processing",
            "015-entity-events", "014-entities");
        (await ScalarAsync<bool>("SELECT to_regclass('harvest_entities') IS NULL AND to_regclass('harvest_entity_flags') IS NULL")).Should().BeTrue();
        (await ConstraintAsync("ck_harvest_post_work_kind")).Should().Contain("extract-event").And.NotContain("entity-resolve");
    }

    [Fact]
    public async Task ApplyPending_ShouldKeepTheEntityChangeFeed_OnATransactionCursorBesideTheEntities()
    {
        Unwrap(await Runner.ApplyPendingAsync("test", CancellationToken.None));

        (await ScalarAsync<string>("SELECT indexdef FROM pg_indexes WHERE indexname = 'ix_harvest_entity_events_cursor'"))
            .Should().Contain("(container_id, tx, id)");
        (await ScalarAsync<string>("SELECT data_type FROM information_schema.columns WHERE table_name = 'harvest_entity_events' AND column_name = 'tx'"))
            .Should().Be("xid8");
        (await ConstraintAsync("ck_harvest_entity_events_type")).Should().Contain("occurrence.predicted").And.Contain("entity.split");
        Guid pool = Guid.NewGuid(), entity = Guid.NewGuid();
        await ExecuteAsync($"""
            INSERT INTO harvest_containers(id,kind,created_at) VALUES('{pool}','pool',now());
            INSERT INTO harvest_entities(id,container_id,entity_type,name,name_key,first_seen_at,last_seen_at,created_at,updated_at)
            VALUES('{entity}','{pool}','event','Tashkent Fashion Week','fashion tashkent week',now(),now(),now(),now());
            INSERT INTO harvest_entity_events(event_id,container_id,entity_id,entity_type,type,occurred_at)
            VALUES(gen_random_uuid(),'{pool}','{entity}','event','entity.created',now());
            """);
        (await ScalarAsync<string>("SELECT payload::text FROM harvest_entity_events")).Should().Be("{}");
        await FailsAsync($"""
            INSERT INTO harvest_entity_events(event_id,container_id,entity_id,entity_type,type,occurred_at)
            VALUES(gen_random_uuid(),'{pool}','{entity}','event','post.new',now())
            """, "ck_harvest_entity_events_type");
        await FailsAsync($"""
            INSERT INTO harvest_entity_events(event_id,container_id,entity_id,entity_type,type,occurred_at)
            VALUES(gen_random_uuid(),'{pool}',gen_random_uuid(),'event','entity.created',now())
            """, "fk_harvest_entity_events_entity");

        Unwrap(await Runner.RollbackAsync(targetOrdinal: 14, CancellationToken.None)).Should().Equal("021-pipeline-auto-off", "020-asset-outcomes", "019-source-catalog", "018-subscription-selectors", "017-processing-reruns", "016-model-processing",
            "015-entity-events");
        (await ScalarAsync<bool>("SELECT to_regclass('harvest_entity_events') IS NULL AND to_regclass('harvest_entities') IS NOT NULL")).Should().BeTrue();
    }

    [Fact]
    public async Task ApplyPending_ShouldMapTheAiSwitchToAModeAndBackOnRollback()
    {
        Unwrap(await Runner.ApplyPendingAsync("test", CancellationToken.None));
        Unwrap(await Runner.RollbackAsync(targetOrdinal: 15, CancellationToken.None)).Should().Equal("021-pipeline-auto-off", "020-asset-outcomes", "019-source-catalog", "018-subscription-selectors", "017-processing-reruns", "016-model-processing");
        (await ColumnCountAsync("table_name = 'harvest_sources' AND column_name = 'model_processing'")).Should().Be(0);
        Guid pool = Guid.NewGuid(), open = Guid.NewGuid(), closed = Guid.NewGuid();
        await ExecuteAsync($"""
            INSERT INTO harvest_containers(id,kind,created_at) VALUES('{pool}','pool',now());
            INSERT INTO harvest_sources(id,container_id,family,handle,coverage,completeness,posture,state_since,created_at,ai_allowed)
            VALUES('{open}','{pool}','site','open.example','whole','complete','own_fetch',now(),now(),true),
                  ('{closed}','{pool}','site','closed.example','whole','complete','own_fetch',now(),now(),false);
            """);

        Unwrap(await Runner.ApplyPendingAsync("test", CancellationToken.None)).Should().Equal("016-model-processing", "017-processing-reruns", "018-subscription-selectors", "019-source-catalog", "020-asset-outcomes", "021-pipeline-auto-off");
        (await ScalarAsync<string>($"SELECT model_processing FROM harvest_sources WHERE id='{open}'")).Should().Be("auto");
        (await ScalarAsync<string>($"SELECT model_processing FROM harvest_sources WHERE id='{closed}'")).Should().Be("off");
        await FailsAsync($"UPDATE harvest_sources SET model_processing='sometimes' WHERE id='{open}'", "ck_harvest_sources_model_processing");
        Guid raw = Guid.NewGuid();
        await ExecuteAsync($"""
            INSERT INTO harvest_sources(id,container_id,family,handle,coverage,completeness,posture,state_since,created_at)
            VALUES('{raw}','{pool}','site','raw.example','whole','complete','own_fetch',now(),now())
            """);
        (await ScalarAsync<string>($"SELECT model_processing FROM harvest_sources WHERE id='{raw}'")).Should()
            .Be("off", "a row inserted without a mode never starts sending posts to a model");
        await ExecuteAsync($"UPDATE harvest_sources SET model_processing='manual',ai_allowed=false WHERE id='{open}'");
        (await ColumnCountAsync("table_name = 'harvest_post_work' AND column_name IN ('run_id', 'explicit')")).Should().Be(2);
        await FailsAsync("INSERT INTO harvest_processing_runs(id,use_cases,scope,created_at) VALUES(gen_random_uuid(),'{post-tags}','some',now())",
            "ck_harvest_processing_runs_scope");
        await FailsAsync("INSERT INTO harvest_processing_runs(id,use_cases,scope,created_at) VALUES(gen_random_uuid(),'{rental-terms}','all',now())",
            "ck_harvest_processing_runs_use_cases");

        Unwrap(await Runner.RollbackAsync(targetOrdinal: 15, CancellationToken.None)).Should().Equal("021-pipeline-auto-off", "020-asset-outcomes", "019-source-catalog", "018-subscription-selectors", "017-processing-reruns", "016-model-processing");
        (await ScalarAsync<bool>($"SELECT ai_allowed FROM harvest_sources WHERE id='{open}'")).Should().BeFalse("a manual source reads as not allowed");
        (await ScalarAsync<bool>("SELECT to_regclass('harvest_processing_runs') IS NULL")).Should().BeTrue();
        (await ColumnCountAsync("table_name = 'harvest_post_work' AND column_name IN ('run_id', 'explicit')")).Should().Be(0);
    }

    [Fact]
    public async Task ApplyPending_ShouldAddSelectorsAndDropThemOnRollback()
    {
        Unwrap(await Runner.ApplyPendingAsync("test", CancellationToken.None));
        Guid tenant = Guid.NewGuid(), pool = Guid.NewGuid(), selector = Guid.NewGuid(), tag = Guid.NewGuid(), other = Guid.NewGuid();
        await ExecuteAsync($$"""
            INSERT INTO harvest_tenants(id,slug,name,created_at) VALUES('{{tenant}}','events','Events',now());
            INSERT INTO harvest_containers(id,kind,created_at) VALUES('{{pool}}','pool',now());
            INSERT INTO harvest_subscriptions(id,tenant_id,container_id,kind,mode,name,selector,selector_version,created_at)
            VALUES('{{selector}}','{{tenant}}','{{pool}}','selector','pull','ocharo-events','{"entityTypes":["event"]}',1,now());
            INSERT INTO harvest_subscriptions(id,tenant_id,container_id,kind,mode,created_at) VALUES('{{tag}}','{{tenant}}','{{pool}}','tag','pull',now());
            INSERT INTO harvest_subscription_selectors(subscription_id,version,selector,created_at)
            VALUES('{{selector}}',1,'{"entityTypes":["event"]}',now());
            INSERT INTO harvest_subscription_event(subscription_id,event_id,item_id,type,occurred_at,matched)
            VALUES('{{selector}}',gen_random_uuid(),'item-1','post.new',now(),'{"selectorVersion":1,"verdict":"match","clauses":[],"context":{} }');
            """);

        (await ConstraintAsync("ck_harvest_subscriptions_kind")).Should().Contain("selector");
        await FailsAsync($$"""
            INSERT INTO harvest_subscriptions(id,tenant_id,container_id,kind,mode,selector,selector_version,created_at)
            VALUES(gen_random_uuid(),'{{tenant}}','{{pool}}','selector','pull','{}',1,now())
            """, "ck_harvest_subscriptions_selector");
        await FailsAsync($$"""
            INSERT INTO harvest_subscriptions(id,tenant_id,container_id,kind,mode,name,created_at)
            VALUES(gen_random_uuid(),'{{tenant}}','{{pool}}','selector','pull','without-selector',now())
            """, "ck_harvest_subscriptions_selector");
        await FailsAsync($$"""
            INSERT INTO harvest_subscriptions(id,tenant_id,container_id,kind,mode,name,selector,selector_version,filter_key,created_at)
            VALUES(gen_random_uuid(),'{{tenant}}','{{pool}}','selector','pull','with-filter','{}',1,'a-filter',now())
            """, "ck_harvest_subscriptions_selector");
        await FailsAsync($$"""
            INSERT INTO harvest_subscriptions(id,tenant_id,container_id,kind,mode,name,created_at)
            VALUES(gen_random_uuid(),'{{tenant}}','{{pool}}','tag','pull','Not A Slug',now())
            """, "ck_harvest_subscriptions_name");
        await FailsAsync($$"""
            INSERT INTO harvest_subscriptions(id,tenant_id,container_id,kind,mode,name,selector,selector_version,created_at)
            VALUES(gen_random_uuid(),'{{tenant}}','{{pool}}','selector','pull','ocharo-events','{}',1,now())
            """, "ux_harvest_subscriptions_tenant_name");
        await FailsAsync($$"""
            INSERT INTO harvest_subscriptions(id,tenant_id,container_id,kind,mode,name,selector,selector_version,created_at)
            VALUES(gen_random_uuid(),'{{tenant}}','{{pool}}','selector','pull','bad-version','{}',0,now())
            """, "ck_harvest_subscriptions_selector");
        await FailsAsync($"INSERT INTO harvest_subscription_selectors(subscription_id,version,selector,created_at) VALUES('{selector}',1,'{{}}',now())",
            "pk_harvest_subscription_selectors");
        await FailsAsync($$"""
            INSERT INTO harvest_subscription_event(subscription_id,event_id,item_id,type,occurred_at,matched)
            VALUES('{{selector}}',gen_random_uuid(),'item-2','post.new',now(),'[]')
            """, "ck_harvest_subscription_event_matched");
        await ExecuteAsync($"UPDATE harvest_subscriptions SET state='archived' WHERE id='{selector}'");
        await ExecuteAsync($$"""
            INSERT INTO harvest_subscriptions(id,tenant_id,container_id,kind,mode,name,selector,selector_version,created_at)
            VALUES('{{other}}','{{tenant}}','{{pool}}','selector','pull','ocharo-events','{}',1,now())
            """);

        Unwrap(await Runner.RollbackAsync(targetOrdinal: 17, CancellationToken.None)).Should().Equal("021-pipeline-auto-off", "020-asset-outcomes", "019-source-catalog", "018-subscription-selectors");

        (await ScalarAsync<bool>("SELECT to_regclass('harvest_subscription_selectors') IS NULL")).Should().BeTrue();
        (await ScalarAsync<long>("SELECT count(*) FROM harvest_subscriptions WHERE kind = 'selector'")).Should().Be(0);
        (await ScalarAsync<string>("SELECT string_agg(id::text, ',') FROM harvest_subscriptions")).Should().Be(tag.ToString(), "a non-selector subscription stays");
        (await ScalarAsync<long>("SELECT count(*) FROM harvest_subscription_event")).Should().Be(0, "the selector subscription's events cascade");
        (await ColumnCountAsync("table_name = 'harvest_subscriptions' AND column_name IN ('name', 'selector', 'selector_version')")).Should().Be(0);
        (await ConstraintAsync("ck_harvest_subscriptions_kind")).Should().NotContain("selector");
        (await ScalarAsync<bool>("SELECT to_regclass('ux_harvest_subscriptions_tenant_name') IS NULL AND to_regclass('ix_harvest_subscriptions_selectors') IS NULL"))
            .Should().BeTrue();

        Unwrap(await Runner.ApplyPendingAsync("test", CancellationToken.None)).Should().Equal("018-subscription-selectors", "019-source-catalog", "020-asset-outcomes", "021-pipeline-auto-off");
        (await ScalarAsync<bool>("SELECT to_regclass('harvest_subscription_selectors') IS NOT NULL")).Should().BeTrue();
    }

    [Fact]
    public async Task ApplyPending_ShouldAddTheCatalogRecordAndTheSourceKey_AndDropThemOnRollback()
    {
        Unwrap(await Runner.ApplyPendingAsync("test", CancellationToken.None));
        Guid pool = Guid.NewGuid(), keyed = Guid.NewGuid(), manual = Guid.NewGuid();
        await ExecuteAsync($$"""
            INSERT INTO harvest_containers(id,kind,created_at) VALUES('{{pool}}','pool',now());
            INSERT INTO harvest_sources(id,container_id,family,handle,coverage,completeness,posture,state_since,created_at,catalog_key)
            VALUES('{{keyed}}','{{pool}}','site','afisha.uz','whole','complete','own_fetch',now(),now(),'ocharo.afisha');
            INSERT INTO harvest_sources(id,container_id,family,handle,coverage,completeness,posture,state_since,created_at)
            VALUES('{{manual}}','{{pool}}','site','by-hand.example','whole','complete','own_fetch',now(),now());
            INSERT INTO harvest_catalog_sources(key,turned_on,changed_by) VALUES('ocharo.afisha',false,'operator-key');
            """);

        (await ScalarAsync<bool>("SELECT changed_at <= now() FROM harvest_catalog_sources WHERE key='ocharo.afisha'")).Should().BeTrue("changed_at defaults to now()");
        (await ScalarAsync<string>($"SELECT catalog_key FROM harvest_sources WHERE id='{keyed}'")).Should().Be("ocharo.afisha");
        (await ScalarAsync<bool>($"SELECT catalog_key IS NULL FROM harvest_sources WHERE id='{manual}'")).Should().BeTrue("a source registered by hand stays outside the catalog");
        (await ScalarAsync<string>("SELECT indexdef FROM pg_indexes WHERE indexname = 'ix_harvest_sources_catalog_key'")).Should().Contain("catalog_key");
        await FailsAsync("INSERT INTO harvest_catalog_sources(key,turned_on,changed_by) VALUES('ocharo.afisha',true,'setup')", "pk_harvest_catalog_sources");
        await FailsAsync("INSERT INTO harvest_catalog_sources(key,turned_on,changed_by) VALUES('ocharo.anons',true,'')", "ck_harvest_catalog_sources_changed_by");

        Unwrap(await Runner.RollbackAsync(targetOrdinal: 18, CancellationToken.None)).Should().Equal("021-pipeline-auto-off", "020-asset-outcomes", "019-source-catalog");
        (await ScalarAsync<bool>("SELECT to_regclass('harvest_catalog_sources') IS NULL AND to_regclass('ix_harvest_sources_catalog_key') IS NULL")).Should().BeTrue();
        (await ColumnCountAsync("table_name = 'harvest_sources' AND column_name = 'catalog_key'")).Should().Be(0);
        (await ScalarAsync<long>("SELECT count(*) FROM harvest_sources")).Should().Be(2, "the rollback forgets the choices, never the sources");

        Unwrap(await Runner.ApplyPendingAsync("test", CancellationToken.None)).Should().Equal("019-source-catalog", "020-asset-outcomes", "021-pipeline-auto-off");
        (await ScalarAsync<bool>("SELECT to_regclass('harvest_catalog_sources') IS NOT NULL")).Should().BeTrue();
        (await ColumnCountAsync("table_name = 'harvest_sources' AND column_name = 'catalog_key'")).Should().Be(1);
    }

    [Fact]
    public async Task ApplyPending_ShouldRecordEachCopysOutcomeAndSize_AndDropThemOnRollbackKeepingTheCopies()
    {
        Unwrap(await Runner.ApplyPendingAsync("test", CancellationToken.None));
        Guid pool = Guid.NewGuid(), source = Guid.NewGuid(), copied = Guid.NewGuid(), queued = Guid.NewGuid(), file = Guid.NewGuid();
        await ExecuteAsync($$"""
            INSERT INTO harvest_containers(id,kind,created_at) VALUES('{{pool}}','pool',now());
            INSERT INTO harvest_sources(id,container_id,family,handle,coverage,completeness,posture,state_since,created_at)
            VALUES('{{source}}','{{pool}}','site','assets.example','whole','complete','own_fetch',now(),now());
            INSERT INTO harvest_source_assets(id,source_id,kind,origin_url,file_id,bytes,outcome,finished_at)
            VALUES('{{copied}}','{{source}}','image','https://assets.example/a.png','{{file}}',1234,'copied',now());
            INSERT INTO harvest_source_assets(id,source_id,kind,origin_url) VALUES('{{queued}}','{{source}}','image','https://assets.example/b.png');
            """);

        (await ColumnCountAsync("table_name = 'harvest_source_assets' AND column_name IN ('bytes', 'outcome', 'finished_at') AND is_nullable = 'YES'")).Should().Be(3);
        (await ScalarAsync<bool>($"SELECT bytes IS NULL AND outcome IS NULL AND finished_at IS NULL FROM harvest_source_assets WHERE id='{queued}'"))
            .Should().BeTrue("a copy made before the release, or not yet tried, stays unrecorded");
        await FailsAsync($"UPDATE harvest_source_assets SET bytes=-1 WHERE id='{queued}'", "ck_harvest_source_assets_bytes");

        Unwrap(await Runner.RollbackAsync(targetOrdinal: 19, CancellationToken.None)).Should().Equal("021-pipeline-auto-off", "020-asset-outcomes");
        (await ColumnCountAsync("table_name = 'harvest_source_assets' AND column_name IN ('bytes', 'outcome', 'finished_at')")).Should().Be(0);
        (await ScalarAsync<long>("SELECT count(*) FROM harvest_source_assets WHERE file_id IS NOT NULL")).Should().Be(1, "the rollback forgets the records, never the copies");

        Unwrap(await Runner.ApplyPendingAsync("test", CancellationToken.None)).Should().Equal("020-asset-outcomes", "021-pipeline-auto-off");
        (await ScalarAsync<bool>($"SELECT bytes IS NULL AND outcome IS NULL FROM harvest_source_assets WHERE id='{copied}'")).Should().BeTrue();
    }

    [Fact]
    public async Task ApplyPending_ShouldAddThePipelineAutoOffColumns_StartTheCountForExistingPipelines_AndDropThemOnRollbackKeepingTheRows()
    {
        Unwrap(await Runner.ApplyPendingAsync("test", CancellationToken.None));
        Unwrap(await Runner.RollbackAsync(targetOrdinal: 20, CancellationToken.None)).Should().Equal("021-pipeline-auto-off");
        Guid pool = Guid.NewGuid(), source = Guid.NewGuid(), existing = Guid.NewGuid(), created = Guid.NewGuid();
        await ExecuteAsync($$"""
            INSERT INTO harvest_containers(id,kind,created_at) VALUES('{{pool}}','pool',now());
            INSERT INTO harvest_sources(id,container_id,family,handle,coverage,completeness,posture,state_since,created_at)
            VALUES('{{source}}','{{pool}}','site','www.afisha.uz','whole','complete','own_fetch',now(),now());
            INSERT INTO harvest_pipelines(id,container_id,source_id,scope_key,state,state_since,created_at)
            VALUES('{{existing}}','{{pool}}','{{source}}','existing','active',now(),now());
            INSERT INTO harvest_schedules(id,container_id,pipeline_id,kind,mode,every_interval,created_at)
            VALUES(gen_random_uuid(),'{{pool}}','{{existing}}','capture','interval',interval '1 hour',now());
            """);

        Unwrap(await Runner.ApplyPendingAsync("test", CancellationToken.None)).Should().Equal("021-pipeline-auto-off");
        await ExecuteAsync($$"""
            INSERT INTO harvest_pipelines(id,container_id,source_id,scope_key,state,state_since,created_at)
            VALUES('{{created}}','{{pool}}','{{source}}','created','active',now(),now())
            """);

        (await ColumnCountAsync("table_name = 'harvest_pipelines' AND column_name IN ('auto_off_after', 'failures_since', 'auto_off_at', "
            + "'auto_off_run_id', 'auto_off_failures', 'auto_off_reason') AND is_nullable = 'YES'")).Should().Be(6);
        (await ScalarAsync<bool>($"SELECT failures_since IS NOT NULL AND auto_off_after IS NULL AND auto_off_at IS NULL FROM harvest_pipelines WHERE id='{existing}'"))
            .Should().BeTrue("runs from before the release never count");
        (await ScalarAsync<bool>($"SELECT failures_since IS NULL FROM harvest_pipelines WHERE id='{created}'"))
            .Should().BeTrue("a new pipeline counts all of its runs");
        await FailsAsync($"UPDATE harvest_pipelines SET auto_off_after=-1 WHERE id='{existing}'", "ck_harvest_pipelines_auto_off_after");
        await FailsAsync($"UPDATE harvest_pipelines SET auto_off_after=1001 WHERE id='{existing}'", "ck_harvest_pipelines_auto_off_after");
        await ExecuteAsync($"UPDATE harvest_pipelines SET auto_off_after=0 WHERE id='{existing}'");
        await ExecuteAsync($"UPDATE harvest_pipelines SET auto_off_after=1000 WHERE id='{existing}'");

        Unwrap(await Runner.RollbackAsync(targetOrdinal: 20, CancellationToken.None)).Should().Equal("021-pipeline-auto-off");
        (await ColumnCountAsync("table_name = 'harvest_pipelines' AND column_name IN ('auto_off_after', 'failures_since', 'auto_off_at', "
            + "'auto_off_run_id', 'auto_off_failures', 'auto_off_reason')")).Should().Be(0);
        (await ScalarAsync<long>("SELECT count(*) FROM harvest_pipelines")).Should().Be(2, "the rollback forgets the thresholds, never the pipelines");
        (await ScalarAsync<long>("SELECT count(*) FROM harvest_schedules")).Should().Be(1);

        Unwrap(await Runner.ApplyPendingAsync("test", CancellationToken.None)).Should().Equal("021-pipeline-auto-off");
        (await ScalarAsync<long>("SELECT count(*) FROM harvest_pipelines WHERE failures_since IS NOT NULL")).Should().Be(2);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task FailsAsync(string sql, string constraint)
    {
        var failure = await FluentActions.Awaiting(() => ExecuteAsync(sql)).Should().ThrowAsync<PostgresException>();
        failure.Which.ConstraintName.Should().Be(constraint);
    }

    private async Task<List<string>> HarvestTablesAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            """
            SELECT c.relname FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace
            WHERE n.nspname = 'public' AND c.relkind IN ('r', 'p') AND NOT c.relispartition
              AND c.relname LIKE 'harvest\_%'
              AND c.relname NOT IN ('harvest_migration_history', 'harvest_migration_history_events')
            """, connection);
        var tables = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
            tables.Add(reader.GetString(0));
        return tables;
    }

    /// <summary>Counts the columns of non-partition <c>harvest_</c> tables that match a condition.</summary>
    private Task<long> ColumnCountAsync(string condition) => ScalarAsync<long>(
        $"""
         SELECT count(*) FROM information_schema.columns
         WHERE table_schema = 'public' AND table_name LIKE 'harvest\_%' AND table_name NOT LIKE 'harvest\_captures\_%' AND {condition}
         """);

    /// <summary>Returns a constraint's definition.</summary>
    private Task<string> ConstraintAsync(string name) =>
        ScalarAsync<string>($"SELECT pg_get_constraintdef(oid) FROM pg_constraint WHERE conname = '{name}'");

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
