namespace WoW.Two.Sdk.Beta.Be.Harvest.Posts;

/// <summary>Represents a capture write, including whether the content projection changed.</summary>
public sealed record CaptureSaveResult
{
    /// <summary>Gets the resolved post after the write.</summary>
    public required PostModel Post { get; init; }
    /// <summary>Gets the source link identifier.</summary>
    public required Guid LinkId { get; init; }
    /// <summary>Gets the new immutable capture, or null when the projection did not change.</summary>
    public Guid? CaptureId { get; init; }
    /// <summary>Gets whether a new capture was written.</summary>
    public required bool Changed { get; init; }
}
