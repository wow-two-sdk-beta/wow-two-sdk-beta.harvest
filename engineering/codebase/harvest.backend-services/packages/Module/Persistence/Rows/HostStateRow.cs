namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence.Rows;

/// <summary>Maps a row of <c>harvest_host_state</c> — one host's politeness gate across every tenant, container and
/// pipeline: its pacing, its <c>Retry-After</c> and pause gates, and its cached <c>robots.txt</c>.</summary>
internal sealed class HostStateRow
{
    public const string TableName = "harvest_host_state";

    public required string HostKey { get; set; }

    public int MinDelayMs { get; set; } = 2000;

    public int AdaptiveDelayMs { get; set; } = 2000;

    public int? CrawlDelayS { get; set; }

    public DateTimeOffset? RetryAfterUntil { get; set; }

    public DateTimeOffset? PausedUntil { get; set; }

    public string? PauseReason { get; set; }

    public int ConsecutiveFailures { get; set; }

    public DateTimeOffset NextAllowedAt { get; set; }

    public short? RobotsStatus { get; set; }

    public string? RobotsBody { get; set; }

    public DateTimeOffset? RobotsCachedAt { get; set; }

    public DateTimeOffset UpdatedAt { get; set; }
}
