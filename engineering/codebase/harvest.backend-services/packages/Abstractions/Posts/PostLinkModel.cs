using System.Text.Json;

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

    /// <summary>Gets when availability was last conclusively observed, including an initial capture.</summary>
    public DateTimeOffset? LastCheckedAtUtc { get; init; }
    /// <summary>Gets when the source link was last confirmed present.</summary>
    public DateTimeOffset? LastSeenAtUtc { get; init; }
    /// <summary>Gets when a cadence-based check becomes eligible, without promising a scheduled execution.</summary>
    public DateTimeOffset? NextCheckEligibleAtUtc { get; init; }
    /// <summary>Gets the latest recorded availability outcome, or null if none exists.</summary>
    public string? LastCheckOutcome { get; init; }
    /// <summary>Gets the HTTP status behind the latest recorded availability observation.</summary>
    public int? LastCheckHttpStatus { get; init; }
    /// <summary>Gets the run identifier that produced the latest observation, when recorded.</summary>
    public Guid? LastCheckRunId { get; init; }
    /// <summary>Gets the originating run kind, so capture evidence is distinguishable from a dedicated check.</summary>
    public string? LastCheckRunKind { get; init; }
    /// <summary>Gets the count of recorded availability observations, excluding inconclusive fetch failures.</summary>
    public int CheckCount { get; init; }
    /// <summary>Gets this link's latest native source field envelope, independently of the merged post's mapped fields.</summary>
    public JsonElement? SourceFields { get; init; }
}
