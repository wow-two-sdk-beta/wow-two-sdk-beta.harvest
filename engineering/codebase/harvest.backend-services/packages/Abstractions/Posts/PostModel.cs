using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Posts;

/// <summary>Represents a resolved post and the source links that support its fields.</summary>
public sealed record PostModel
{
    /// <summary>Gets the post identifier.</summary>
    public required Guid Id { get; init; }
    /// <summary>Gets the container that owns the post.</summary>
    public required Guid ContainerId { get; init; }
    /// <summary>Gets the rolled-up existence state.</summary>
    public required PostState State { get; init; }
    /// <summary>Gets the monotonic post revision.</summary>
    public required int Revision { get; init; }
    /// <summary>Gets the resolved base fields.</summary>
    public required PostEnvelope Base { get; init; }
    /// <summary>Gets the link that supplied each resolved field.</summary>
    public IReadOnlyDictionary<string, Guid> FieldSources { get; init; } = new Dictionary<string, Guid>();
    /// <summary>Gets the post's links, including links that disappeared.</summary>
    public IReadOnlyList<PostLinkModel> Links { get; init; } = [];
    /// <summary>Gets the time the post was first captured.</summary>
    public required DateTimeOffset FirstSeenAtUtc { get; init; }
    /// <summary>Gets the last material change time.</summary>
    public required DateTimeOffset LastChangedAtUtc { get; init; }
    /// <summary>Gets the surviving post when this post was merged.</summary>
    public Guid? MergedInto { get; init; }
}
