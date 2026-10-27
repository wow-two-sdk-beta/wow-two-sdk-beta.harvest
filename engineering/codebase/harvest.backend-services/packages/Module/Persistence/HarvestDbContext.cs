using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence.Rows;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;

/// <summary>The Harvest module's context — a pure mapper over the SQL-owned schema in <c>Migrations/</c>, on snake_case
/// columns.</summary>
/// <remarks>Internal: consumers reach the store through the contract's brokers, never its tables. It maps the registry
/// tables; each lane maps the tables it owns as it lands.</remarks>
/// <param name="options">The context options, configured by <c>AddHarvest</c>.</param>
internal sealed class HarvestDbContext(DbContextOptions<HarvestDbContext> options) : DbContext(options)
{
    public DbSet<TenantRow> Tenants => Set<TenantRow>();

    public DbSet<ApiKeyRow> ApiKeys => Set<ApiKeyRow>();

    public DbSet<ContainerRow> Containers => Set<ContainerRow>();

    public DbSet<CheckPolicyRow> CheckPolicies => Set<CheckPolicyRow>();

    public DbSet<SourceRow> Sources => Set<SourceRow>();

    public DbSet<PipelineRow> Pipelines => Set<PipelineRow>();

    public DbSet<ScheduleRow> Schedules => Set<ScheduleRow>();

    public DbSet<HostStateRow> HostStates => Set<HostStateRow>();

    public DbSet<HostLaneRow> HostLanes => Set<HostLaneRow>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var tenant = modelBuilder.Entity<TenantRow>().ToTable(TenantRow.TableName);
        tenant.HasKey(row => row.Id);
        tenant.Property(row => row.State).HasConversion(Text<TenantState>());

        var key = modelBuilder.Entity<ApiKeyRow>().ToTable(ApiKeyRow.TableName);
        key.HasKey(row => row.Id);

        var container = modelBuilder.Entity<ContainerRow>().ToTable(ContainerRow.TableName);
        container.HasKey(row => row.Id);
        container.Property(row => row.Kind).HasConversion(Text<ContainerKind>());
        container.Property(row => row.State).HasConversion(Text<ContainerState>());

        var checkPolicy = modelBuilder.Entity<CheckPolicyRow>().ToTable(CheckPolicyRow.TableName);
        checkPolicy.HasKey(row => row.Id);

        var source = modelBuilder.Entity<SourceRow>().ToTable(SourceRow.TableName);
        source.HasKey(row => row.Id);
        source.Property(row => row.Family).HasConversion(Text<SourceFamily>());
        source.Property(row => row.Coverage).HasConversion(Text<SourceCoverage>());
        source.Property(row => row.Completeness).HasConversion(Text<SourceCompleteness>());
        source.Property(row => row.Posture).HasConversion(Text<SourcePosture>());
        source.Property(row => row.Permission).HasConversion(Text<SourcePermission>());
        source.Property(row => row.RobotsPolicy).HasConversion(Text<RobotsPolicy>());
        source.Property(row => row.ConsentBasis).HasConversion(Text<ConsentBasis>());
        source.Property(row => row.State).HasConversion(Text<SourceState>());
        Jsonb(source.Property(row => row.CapabilityPolicy));

        var pipeline = modelBuilder.Entity<PipelineRow>().ToTable(PipelineRow.TableName);
        pipeline.HasKey(row => row.Id);
        pipeline.Property(row => row.State).HasConversion(Text<PipelineState>());
        pipeline.Property(row => row.PauseReason).HasConversion(Text<PipelinePauseReason>());
        pipeline.Property(row => row.AiMode).HasConversion(Text<AiMode>());
        Jsonb(pipeline.Property(row => row.Scope));
        Jsonb(pipeline.Property(row => row.Limits));
        Jsonb(pipeline.Property(row => row.StepOverrides));

        var schedule = modelBuilder.Entity<ScheduleRow>().ToTable(ScheduleRow.TableName);
        schedule.HasKey(row => row.Id);
        schedule.Property(row => row.Kind).HasConversion(Text<WorkKind>());
        schedule.Property(row => row.Mode).HasConversion(Text<ScheduleMode>());

        var hostState = modelBuilder.Entity<HostStateRow>().ToTable(HostStateRow.TableName);
        hostState.HasKey(row => row.HostKey);

        var hostLane = modelBuilder.Entity<HostLaneRow>().ToTable(HostLaneRow.TableName);
        hostLane.HasKey(row => new { row.HostKey, row.Lane });
        // The lane's host_key references the host row; declaring it orders inserts host-before-lane.
        hostLane.HasOne<HostStateRow>().WithMany().HasForeignKey(row => row.HostKey).HasPrincipalKey(row => row.HostKey);
    }

    /// <summary>Converts an enum to the snake_case text the schema's CHECK constraints name.</summary>
    private static ValueConverter<TEnum, string> Text<TEnum>() where TEnum : struct, Enum =>
        new(value => SnakeCaseEnum<TEnum>.ToText(value), text => SnakeCaseEnum<TEnum>.Parse(text));

    /// <summary>Maps a JSON text property onto a jsonb column.</summary>
    private static void Jsonb(PropertyBuilder<string> property) => property.HasColumnType("jsonb");
}
