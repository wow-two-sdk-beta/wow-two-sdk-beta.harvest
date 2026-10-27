namespace WoW.Two.Sdk.Beta.Be.Harvest.Posts;

/// <summary>Represents a recorded merge whose original link membership can be restored.</summary>
public sealed record PostMergeModel
{
    /// <summary>Gets the merge identifier used to undo this operation.</summary>
    public required Guid Id { get; init; }
    /// <summary>Gets the post that receives the links.</summary>
    public required Guid SurvivorId { get; init; }
    /// <summary>Gets the post whose links moved.</summary>
    public required Guid LoserId { get; init; }
    /// <summary>Gets whether the original link membership was restored.</summary>
    public required bool Undone { get; init; }
}
