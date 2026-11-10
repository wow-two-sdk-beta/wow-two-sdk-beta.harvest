namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Represents a request to declare a group of sources in a container; declaring a taken name returns that
/// group unchanged.</summary>
public sealed record SourceGroupDeclaration
{
    /// <summary>Gets the container the group belongs to.</summary>
    public required Guid ContainerId { get; init; }

    /// <summary>Gets the group's name, 1–200 characters after trimming.</summary>
    public required string Name { get; init; }

    /// <summary>Gets what the group is, at most <see cref="SourceConstants.MaxGroupDescriptionLength"/> characters.</summary>
    public string? Description { get; init; }
}
