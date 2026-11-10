namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Represents a group of sources in a container, such as a publisher whose channels or branches are its
/// sources.</summary>
public sealed record SourceGroupModel
{
    /// <summary>Gets the group's identifier.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the container the group belongs to.</summary>
    public required Guid ContainerId { get; init; }

    /// <summary>Gets the group's name, unique in its container without regard to case.</summary>
    public required string Name { get; init; }

    /// <summary>Gets what the group is.</summary>
    public string? Description { get; init; }
}
