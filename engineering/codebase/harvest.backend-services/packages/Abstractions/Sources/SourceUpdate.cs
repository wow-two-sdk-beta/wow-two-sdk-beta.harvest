namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Represents a change to a source's display name and group; an omitted value keeps what is stored.</summary>
public sealed record SourceUpdate
{
    /// <summary>Gets the display name; a blank one clears it, so the source shows its handle again.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the group to move the source into; it must belong to the source's container.</summary>
    public Guid? GroupId { get; init; }

    /// <summary>Gets whether to take the source out of its group; only with no <see cref="GroupId"/>.</summary>
    public bool ClearGroup { get; init; }
}
