using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence.Rows;

/// <summary>Maps a row of <c>harvest_schedules</c>.</summary>
internal sealed class ScheduleRow
{
    public const string TableName = "harvest_schedules";

    public required Guid Id { get; set; }

    public required Guid ContainerId { get; set; }

    public required Guid PipelineId { get; set; }

    public required WorkKind Kind { get; set; }

    public required ScheduleMode Mode { get; set; }

    public TimeSpan? EveryInterval { get; set; }

    public TimeSpan? MaxInterval { get; set; }

    public string? Cron { get; set; }

    public required string TimeZone { get; set; }

    public TimeOnly? WindowStart { get; set; }

    public TimeOnly? WindowEnd { get; set; }

    public required bool Enabled { get; set; }

    public DateTimeOffset? NextDueAt { get; set; }

    public DateTimeOffset? LastQueuedAt { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}
