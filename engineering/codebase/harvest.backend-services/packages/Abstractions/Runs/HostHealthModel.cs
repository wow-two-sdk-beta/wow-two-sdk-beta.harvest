namespace WoW.Two.Sdk.Beta.Be.Harvest.Runs;

/// <summary>Represents an engine host lane's current pause and queue.</summary>
public sealed record HostHealthModel
{
    /// <summary>Gets the source host.</summary>
    public required string HostKey { get; init; }
    /// <summary>Gets the step lane.</summary>
    public required string Lane { get; init; }
    /// <summary>Gets when the lane may work again.</summary>
    public DateTimeOffset? PausedUntilUtc { get; init; }
    /// <summary>Gets the lane's consecutive failures.</summary>
    public int ConsecutiveFailures { get; init; }
    /// <summary>Gets the last challenge kind.</summary>
    public string? LastChallengeKind { get; init; }
    /// <summary>Gets the number of queued items.</summary>
    public int Queued { get; init; }
    /// <summary>Gets the number of running items.</summary>
    public int Running { get; init; }
}
