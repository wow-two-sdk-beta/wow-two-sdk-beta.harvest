namespace WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

/// <summary>Controls one configured stage of a source section.</summary>
public sealed record PipelineStageOptions
{
    /// <summary>Gets the batch size for detail captures or processing inputs. Discovery ignores this value.</summary>
    public int BatchSize { get; init; }

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
}
