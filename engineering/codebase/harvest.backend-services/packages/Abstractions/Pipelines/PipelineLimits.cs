namespace WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

/// <summary>Represents a pipeline's per-run limits; a null keeps the adapter's default.</summary>
public sealed record PipelineLimits
{
    /// <summary>Gets the most listing pages a filtered run reads, 1–100.</summary>
    public int? MaxPages { get; init; }

    /// <summary>Gets the most items a filtered run captures, 1–2,000.</summary>
    public int? MaxItems { get; init; }

    /// <summary>Gets the most requests a sweep makes.</summary>
    public int? MaxRequests { get; init; }

    /// <summary>Gets the most bytes a sweep reads.</summary>
    public long? MaxBytes { get; init; }

    /// <summary>Gets the longest a sweep runs.</summary>
    public TimeSpan? MaxDuration { get; init; }

    /// <summary>Gets the most existence probes a check run makes.</summary>
    public int? MaxProbes { get; init; }
}
