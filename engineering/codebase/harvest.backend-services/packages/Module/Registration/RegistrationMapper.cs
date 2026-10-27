using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence.Rows;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Registration;

/// <summary>Maps registry rows to the contract's models.</summary>
internal static class RegistrationMapper
{
    public static TenantModel ToModel(this TenantRow row) => new()
    {
        Id = row.Id,
        Slug = row.Slug,
        Name = row.Name,
        State = row.State,
        PlatformCredentialsAllowed = row.PlatformCredentialsAllowed,
        CreatedAtUtc = row.CreatedAt,
    };

    public static ApiKeyModel ToModel(this ApiKeyRow row) => new()
    {
        Id = row.Id,
        TenantId = row.TenantId,
        Name = row.Name,
        Prefix = row.Prefix,
        Scopes = row.Scopes,
        State = row.RevokedAt is null ? ApiKeyState.Active : ApiKeyState.Revoked,
        CreatedAtUtc = row.CreatedAt,
        ExpiresAtUtc = row.ExpiresAt,
        LastUsedAtUtc = row.LastUsedAt,
        RevokedAtUtc = row.RevokedAt,
    };

    public static ContainerModel ToModel(this ContainerRow row) => new()
    {
        Id = row.Id,
        Kind = row.Kind,
        TenantId = row.TenantId,
        ExternalRef = row.ExternalRef,
        Residency = row.Residency,
        EmbeddingsEnabled = row.EmbeddingsEnabled,
        State = row.State,
        CreatedAtUtc = row.CreatedAt,
    };

    public static SourceModel ToModel(this SourceRow row, string? checkPolicy) => new()
    {
        Id = row.Id,
        ContainerId = row.ContainerId,
        Family = row.Family,
        Handle = row.Handle,
        ExternalRef = row.ExternalRef,
        Coverage = row.Coverage,
        Completeness = row.Completeness,
        Posture = row.Posture,
        Permission = row.Permission,
        AiAllowed = row.AiAllowed,
        PoolShareable = row.PoolShareable,
        RobotsPolicy = row.RobotsPolicy,
        ConsentBasis = row.ConsentBasis,
        Authority = row.Authority,
        RetentionClass = row.RetentionClass,
        CheckPolicy = checkPolicy,
        CapabilityPolicy = StoreJson.Read(row.CapabilityPolicy, new CapabilityPolicy()),
        State = row.State,
        StateSinceUtc = row.StateSince,
        CreatedAtUtc = row.CreatedAt,
    };

    public static PipelineModel ToModel(this PipelineRow row, IEnumerable<ScheduleRow> schedules) => new()
    {
        Id = row.Id,
        ContainerId = row.ContainerId,
        SourceId = row.SourceId,
        ScopeKey = row.ScopeKey,
        Scope = StoreJson.Read(row.Scope, new PipelineScope()),
        Name = row.Name,
        State = row.State,
        PauseReason = row.PauseReason,
        Pinned = row.Pinned,
        Ai = row.AiMode,
        Limits = StoreJson.Read(row.Limits, new PipelineLimits()),
        Steps = StoreJson.Read<IReadOnlyDictionary<string, StepOverride>>(row.StepOverrides, new Dictionary<string, StepOverride>()),
        Schedules = [.. schedules.OrderBy(schedule => schedule.Kind).Select(ToModel)],
        CreatedAtUtc = row.CreatedAt,
    };

    public static ScheduleModel ToModel(this ScheduleRow row) => new()
    {
        Id = row.Id,
        Kind = row.Kind,
        Mode = row.Mode,
        Every = row.EveryInterval,
        MaxEvery = row.MaxInterval,
        Cron = row.Cron,
        TimeZone = row.TimeZone,
        Window = row.WindowStart is { } start && row.WindowEnd is { } end ? new ScheduleWindow { Start = start, End = end } : null,
        Enabled = row.Enabled,
        NextDueAtUtc = row.NextDueAt,
    };
}
