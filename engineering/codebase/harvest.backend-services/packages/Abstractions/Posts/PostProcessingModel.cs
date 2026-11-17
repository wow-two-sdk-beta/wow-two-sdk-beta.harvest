using System.Text.Json;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Posts;

/// <summary>A processor result for a saved capture, kept separate from the source's assertions.</summary>
public sealed record PostProcessingModel
{
    /// <summary>Whether this failed configuration requires review instead of another automatic model call.</summary>
    public bool RequiresReview { get; init; }
    /// <summary>Gets the immutable input capture.</summary>
    public required Guid CaptureId { get; init; }

    /// <summary>Gets the source that supplied the input.</summary>
    public required Guid SourceId { get; init; }

    /// <summary>Gets the source link.</summary>
    public required Guid LinkId { get; init; }

    /// <summary>Gets the run that owns this result.</summary>
    public required Guid RunId { get; init; }

    /// <summary>Gets the processor and pinned configuration identity.</summary>
    public required string Processor { get; init; }

    /// <summary>Gets whether the input is still the link's current reading.</summary>
    public required bool IsCurrent { get; init; }

    /// <summary>Gets when processing completed; absent while a result is pending or failed.</summary>
    public DateTimeOffset? ProcessedAtUtc { get; init; }

    /// <summary>Gets the associated run's status.</summary>
    public required string RunStatus { get; init; }

    /// <summary>Gets the versioned processor output, including its evidence and inference provenance when available.</summary>
    public JsonElement? Output { get; init; }
}
