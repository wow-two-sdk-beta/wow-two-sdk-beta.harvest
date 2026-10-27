namespace Harvest.Api.Posts.Controllers;

/// <summary>Identifies the survivor and losing post of a reviewed merge.</summary>
public sealed record MergePostsRequest
{
    /// <summary>Gets the posts' container.</summary>
    public required Guid ContainerId { get; init; }
    /// <summary>Gets the canonical post to retain.</summary>
    public required Guid SurvivorId { get; init; }
    /// <summary>Gets the post whose links move to the survivor.</summary>
    public required Guid LoserId { get; init; }
}
