namespace WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

/// <summary>Asks what a selector would deliver over one container.</summary>
public sealed record SubscriptionPreviewRequest
{
    /// <summary>Gets the tenant that previews; the container is the pool or the tenant's own.</summary>
    public required Guid TenantId { get; init; }

    /// <summary>Gets the container whose posts are judged.</summary>
    public required Guid ContainerId { get; init; }

    /// <summary>Gets the selector to judge, saved or not.</summary>
    public required SubscriptionSelector Selector { get; init; }

    /// <summary>Gets the consumer schema the selector would deliver, such as <c>wow2.event.v1</c>; the event schema refuses
    /// posts that are not events, and null applies no schema rule.</summary>
    public string? Schema { get; init; }

    /// <summary>Gets the start of the window of changed posts; defaults to
    /// <see cref="SelectorRules.DefaultWindowDays"/> days back and is at most <see cref="SelectorRules.MaxWindowDays"/>.</summary>
    public DateTimeOffset? SinceUtc { get; init; }

    /// <summary>Gets how many sample matches to return, 0 to 50.</summary>
    public int Samples { get; init; } = 20;
}

/// <summary>Represents what a selector would deliver over a window.</summary>
public sealed record SubscriptionPreviewModel
{
    /// <summary>Gets the window's start as judged.</summary>
    public required DateTimeOffset SinceUtc { get; init; }

    /// <summary>Gets how many live posts the window held.</summary>
    public int Considered { get; init; }

    /// <summary>Gets how many of them carry a Layer 1 answer.</summary>
    public int Tagged { get; init; }

    /// <summary>Gets how many posts the selector matches.</summary>
    public int Matched { get; init; }

    /// <summary>Gets how many posts the selector refuses.</summary>
    public int NoMatch { get; init; }

    /// <summary>Gets how many posts the selector cannot judge yet.</summary>
    public int Pending { get; init; }

    /// <summary>Gets whether the window held more posts than were judged.</summary>
    public bool Truncated { get; init; }

    /// <summary>Gets the counts per source of each post's context link.</summary>
    public IReadOnlyList<PreviewSourceCount> Sources { get; init; } = [];

    /// <summary>Gets what each clause adds or removes.</summary>
    public IReadOnlyList<PreviewClauseCount> Clauses { get; init; } = [];

    /// <summary>Gets the newest matching posts.</summary>
    public IReadOnlyList<PreviewPost> Samples { get; init; } = [];

    /// <summary>Gets the newest refused posts that one relaxation of the selector would admit.</summary>
    public IReadOnlyList<PreviewPost> NearMisses { get; init; } = [];
}

/// <summary>Represents a source's share of a preview.</summary>
public sealed record PreviewSourceCount
{
    /// <summary>Gets the source.</summary>
    public required Guid SourceId { get; init; }

    /// <summary>Gets the source's display name.</summary>
    public string? Name { get; init; }

    /// <summary>Gets the source's group.</summary>
    public Guid? GroupId { get; init; }

    /// <summary>Gets how many posts came through this source.</summary>
    public int Considered { get; init; }

    /// <summary>Gets how many of them match.</summary>
    public int Matched { get; init; }

    /// <summary>Gets how many of them are pending.</summary>
    public int Pending { get; init; }
}

/// <summary>Represents what one clause changes in a preview.</summary>
public sealed record PreviewClauseCount
{
    /// <summary>Gets the clause, such as <c>domain:style-fashion</c> or <c>except:style-fashion/beauty</c>.</summary>
    public required string Clause { get; init; }

    /// <summary>Gets how many matched posts stop matching when this subject clause alone is removed.</summary>
    public int Adds { get; init; }

    /// <summary>Gets how many posts match once this exclusion alone is removed.</summary>
    public int Removes { get; init; }
}

/// <summary>Represents one post a preview names.</summary>
public sealed record PreviewPost
{
    /// <summary>Gets the post.</summary>
    public required Guid PostId { get; init; }

    /// <summary>Gets the revision that was judged.</summary>
    public required int Revision { get; init; }

    /// <summary>Gets the title as the source gave it.</summary>
    public string? Title { get; init; }

    /// <summary>Gets SCFD's English title from the Layer 1 answer.</summary>
    public string? TitleEn { get; init; }

    /// <summary>Gets the verdict.</summary>
    public required SelectorVerdict Verdict { get; init; }

    /// <summary>Gets the clauses behind the verdict, or for a near miss the relaxation that admits it.</summary>
    public IReadOnlyList<string> Clauses { get; init; } = [];

    /// <summary>Gets when the post last changed (UTC).</summary>
    public required DateTimeOffset LastChangedAtUtc { get; init; }
}
