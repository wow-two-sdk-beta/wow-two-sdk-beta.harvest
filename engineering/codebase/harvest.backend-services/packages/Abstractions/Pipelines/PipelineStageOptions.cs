namespace WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

/// <summary>Controls one configured stage of a source section.</summary>
public sealed record PipelineStageOptions
{
    /// <summary>Gets the batch size for detail captures (1–<see cref="PipelineConstants.MaxHarvestBatch"/>) or processing inputs
    /// (1–<see cref="PipelineConstants.MaxItemsCap"/>). Discovery ignores this value.</summary>
    public int BatchSize { get; init; }

    /// <summary>Gets how a discover stage walks its catalog: <see cref="DiscoveryWalk.Resume"/> at the cursor (null means this),
    /// <see cref="DiscoveryWalk.Full"/> from the first page to the last, or <see cref="DiscoveryWalk.New"/> from the first page
    /// until a page holds nothing new. Discover rows only; a full or new walk drops the pipeline's request budget.</summary>
    public DiscoveryWalk? Walk { get; init; }

    /// <summary>Gets the gap in milliseconds between the starts of the stage's requests to its host, 0–<see cref="PipelineConstants.MaxGapMs"/>;
    /// null means the host's pace. It replaces the host's pace, the reader's gap and the two-second fetch minimum for this
    /// pipeline's own requests; a robots <c>Crawl-delay</c>, a <c>Retry-After</c>, a host pause and a delay a refusal raised still
    /// bind. Discover and harvest rows; only an operator sets it, as only an operator sets the host's pace.</summary>
    public int? GapMs { get; init; }

    /// <summary>Gets how many of its own items a harvest run fetches at once, 1–<see cref="PipelineConstants.MaxParallel"/>; null
    /// means 1. Harvest rows only; only an operator sets it.</summary>
    public int? Parallel { get; init; }

    /// <summary>Gets whether the stage is turned off. Omit this when updating its other settings.</summary>
    public bool? Disabled { get; init; }

    /// <summary>Reads legacy requests so the registry can reject a discovery URL override.</summary>
    public string? CatalogUrl { get; init; }

    /// <summary>Gets the first local date eligible for automatic runs in the schedule's time zone.</summary>
    public DateOnly? StartsOn { get; init; }

    /// <summary>Gets a case-insensitive title filter over saved captures.</summary>
    public string? TitleContains { get; init; }

    /// <summary>Gets the earliest source publication instant to process.</summary>
    public DateTimeOffset? PublishedSinceUtc { get; init; }

    /// <summary>Gets the processor key. Use the model-free summary by default.</summary>
    public string? Processor { get; init; }

    /// <summary>Gets the configured model route, when the processor calls a model.</summary>
    public string? Model { get; init; }

    /// <summary>Gets the immutable prompt version to use for model processing, frozen when the run is queued.</summary>
    public int? PromptVersion { get; init; }
}
