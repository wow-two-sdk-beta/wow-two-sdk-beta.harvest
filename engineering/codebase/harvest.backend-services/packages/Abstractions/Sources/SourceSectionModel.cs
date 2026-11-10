namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Represents a section of a source: one pipeline over it, with its cadence, last run and reach.</summary>
public sealed record SourceSectionModel
{
    /// <summary>Gets the section's pipeline.</summary>
    public required Guid PipelineId { get; init; }

    /// <summary>Gets the pipeline's display name.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the listing URL the section starts from; null for the whole source.</summary>
    public string? Url { get; init; }

    /// <summary>Gets whether the pipeline is active and has an enabled capture schedule.</summary>
    public required bool Harvesting { get; init; }

    /// <summary>Gets whether the pipeline is paused.</summary>
    public required bool Paused { get; init; }

    /// <summary>Gets the enabled capture schedule's interval; null when capture is off or runs on a cron.</summary>
    public TimeSpan? CaptureEvery { get; init; }

    /// <summary>Gets the enabled check schedule's interval; null when checking is off or runs on a cron.</summary>
    public TimeSpan? CheckEvery { get; init; }

    /// <summary>Gets the most listing pages a run reads; null keeps the adapter's default.</summary>
    public int? MaxPages { get; init; }

    /// <summary>Gets the pipeline's newest run of any kind.</summary>
    public SourceSectionRunModel? LastRun { get; init; }

    /// <summary>Gets when the next capture is due (UTC); null unless the section is harvesting and the scheduler has
    /// computed it.</summary>
    public DateTimeOffset? NextDueAt { get; init; }

    /// <summary>Gets the posts whose first capture from this source came from this pipeline's runs.</summary>
    public required int Posts { get; init; }

    /// <summary>Gets the active subscriptions that follow this pipeline.</summary>
    public required int Subscribers { get; init; }
}
