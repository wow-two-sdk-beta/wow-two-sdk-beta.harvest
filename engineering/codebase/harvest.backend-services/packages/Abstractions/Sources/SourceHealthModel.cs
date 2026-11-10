namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Represents how a source's host is read: its pace, its pause and its failures.</summary>
/// <remarks>The host's state is shared by every source and container on that host.</remarks>
public sealed record SourceHealthModel
{
    /// <summary>Gets the host key, the source's handle.</summary>
    public required string HostKey { get; init; }

    /// <summary>Gets the least gap between two requests to the host, in milliseconds.</summary>
    public required int MinDelayMs { get; init; }

    /// <summary>Gets when the host may be read again (UTC); null when it is not paused.</summary>
    public DateTimeOffset? PausedUntil { get; init; }

    /// <summary>Gets why the host is paused, as recorded; null when it is not paused.</summary>
    public string? PauseReason { get; init; }

    /// <summary>Gets the host's failures in a row, the worst of the host and its lanes.</summary>
    public required int ConsecutiveFailures { get; init; }

    /// <summary>Gets the source's newest run that a refusal or a challenge stopped.</summary>
    public SourceRefusalModel? LastRefusal { get; init; }
}
