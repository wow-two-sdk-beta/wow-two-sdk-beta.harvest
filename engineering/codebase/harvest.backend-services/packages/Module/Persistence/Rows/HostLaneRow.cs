namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence.Rows;

/// <summary>Maps a row of <c>harvest_host_lane</c> — one host and lane's back-off state: its run of failures, the last
/// challenge seen, and its pause gate.</summary>
internal sealed class HostLaneRow
{
    public const string TableName = "harvest_host_lane";

    public required string HostKey { get; set; }

    public required string Lane { get; set; }

    public int ConsecutiveFailures { get; set; }

    public string? LastChallengeKind { get; set; }

    public DateTimeOffset? PausedUntil { get; set; }

    public double SuccessEwma { get; set; } = 1;

    public DateTimeOffset UpdatedAt { get; set; }
}
