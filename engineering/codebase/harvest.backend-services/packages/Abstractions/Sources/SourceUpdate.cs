namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Represents a change to a source's name, group, master pause or model access; omitted values keep what is stored.</summary>
public sealed record SourceUpdate
{
    /// <summary>Gets the display name; a blank one clears it, so the source shows its handle again.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the group to move the source into; it must belong to the source's container.</summary>
    public Guid? GroupId { get; init; }

    /// <summary>Gets whether to take the source out of its group; only with no <see cref="GroupId"/>.</summary>
    public bool ClearGroup { get; init; }

    /// <summary>Gets whether the operator pauses the source, preserving every pipeline's own state and schedules.
    /// Only active or operator-paused sources may change this switch.</summary>
    public bool? Paused { get; init; }

    /// <summary>Gets whether model processors and embeddings may read the source's text. A Process stage still selects
    /// its processor and model route.</summary>
    public bool? AiAllowed { get; init; }
}
