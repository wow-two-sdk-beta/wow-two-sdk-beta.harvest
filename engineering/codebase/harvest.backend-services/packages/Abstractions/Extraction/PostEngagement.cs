namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Represents a snapshot of a post's counters, outside its content hash; hidden counts are null.</summary>
public sealed record PostEngagement
{
    /// <summary>Gets the views.</summary>
    public long? Views { get; init; }

    /// <summary>Gets the reactions.</summary>
    public long? Reactions { get; init; }

    /// <summary>Gets the comments.</summary>
    public long? Comments { get; init; }

    /// <summary>Gets the shares or forwards.</summary>
    public long? Shares { get; init; }

    /// <summary>Gets when the counters were read (UTC).</summary>
    public DateTimeOffset? AtUtc { get; init; }
}
