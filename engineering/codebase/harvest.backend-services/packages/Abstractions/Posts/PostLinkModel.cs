namespace WoW.Two.Sdk.Beta.Be.Harvest.Posts;

/// <summary>Represents one source's view of a post and its latest immutable capture.</summary>
public sealed record PostLinkModel
{
    /// <summary>Gets the link identifier.</summary>
    public required Guid Id { get; init; }
    /// <summary>Gets the source identifier.</summary>
    public required Guid SourceId { get; init; }
    /// <summary>Gets the source-local identity.</summary>
    public required string ExternalKey { get; init; }
    /// <summary>Gets the public URL.</summary>
    public required string Url { get; init; }
    /// <summary>Gets the link's existence state.</summary>
    public required LinkState State { get; init; }
    /// <summary>Gets how the source presents the post.</summary>
    public required LinkRole Role { get; init; }
    /// <summary>Gets the latest immutable capture identifier.</summary>
    public Guid? CaptureId { get; init; }
    /// <summary>Gets the capture's partition timestamp.</summary>
    public DateTimeOffset? CapturedAtUtc { get; init; }
}
