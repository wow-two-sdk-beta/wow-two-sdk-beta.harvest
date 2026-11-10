namespace WoW.Two.Sdk.Beta.Be.Harvest.Posts;

/// <summary>Represents one revision of a post: when its revision rose and which base fields changed.</summary>
public sealed record PostRevisionModel
{
    /// <summary>Gets the revision the post reached.</summary>
    public required int Revision { get; init; }

    /// <summary>Gets when the post reached it (UTC).</summary>
    public required DateTimeOffset At { get; init; }

    /// <summary>Gets the top-level base field names whose value changed, or <see cref="PostRevisionConstants.Created"/>
    /// alone for the first revision; empty when only the post's state or provenance moved.</summary>
    public required IReadOnlyList<string> Changed { get; init; }
}
