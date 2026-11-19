using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;

/// <summary>Represents a request to compare a selector subscription's verdicts with a registered filter's over the same
/// posts, so a shadow period can show where the two disagree before a consumer switches.</summary>
public sealed record SelectorDiffRequest
{
    /// <summary>Gets the selector subscription whose current selector is judged.</summary>
    public required Guid SubscriptionId { get; init; }

    /// <summary>Gets the key of the registered post filter to compare against; a tag filter key such as <c>tag:concert</c>
    /// also counts. An unregistered key is refused.</summary>
    public required string FilterKey { get; init; }

    /// <summary>Gets the earliest last change of the posts compared; seven days back when omitted, at most
    /// <see cref="SelectorRules.MaxWindowDays"/> days.</summary>
    public DateTimeOffset? SinceUtc { get; init; }

    /// <summary>Gets how many disagreements to list: 1 to 1,000; the counts always cover every post.</summary>
    public int Limit { get; init; } = 50;

    /// <summary>Gets whether to list disagreements at all; false answers with the counts alone.</summary>
    public bool Items { get; init; } = true;

    /// <summary>Gets whether the list also names the posts the selector cannot judge yet, after the disagreements.</summary>
    public bool IncludePending { get; init; }
}

/// <summary>Represents the counts and the disagreements of a selector subscription set against a filter.</summary>
/// <remarks>Each post compared falls in exactly one count, in this order: <see cref="OutOfScope"/>, then
/// <see cref="Pending"/>, then <see cref="FilterUndecided"/>, then one of <see cref="Both"/>, <see cref="FilterOnly"/>,
/// <see cref="SelectorOnly"/> and <see cref="Neither"/>; <see cref="Posts"/> is their sum.</remarks>
public sealed record SelectorDiffModel
{
    /// <summary>Gets the subscription's name.</summary>
    public required string Subscription { get; init; }

    /// <summary>Gets the version of the selector that was judged.</summary>
    public required int SelectorVersion { get; init; }

    /// <summary>Gets the key of the filter compared against.</summary>
    public required string FilterKey { get; init; }

    /// <summary>Gets the earliest last change of the posts compared.</summary>
    public required DateTimeOffset SinceUtc { get; init; }

    /// <summary>Gets how many posts of the container changed since <see cref="SinceUtc"/>, merged and purged posts aside.</summary>
    public int Posts { get; init; }

    /// <summary>Gets how many posts none of whose links the selector's sources admit; neither side delivers them.</summary>
    public int OutOfScope { get; init; }

    /// <summary>Gets how many posts both the selector and the filter pass.</summary>
    public int Both { get; init; }

    /// <summary>Gets how many posts the filter passes and the selector refuses.</summary>
    public int FilterOnly { get; init; }

    /// <summary>Gets how many posts the selector passes and the filter refuses.</summary>
    public int SelectorOnly { get; init; }

    /// <summary>Gets how many posts both refuse.</summary>
    public int Neither { get; init; }

    /// <summary>Gets how many in-scope posts the selector cannot judge yet, whatever the filter says: they wait for their tags.</summary>
    public int Pending { get; init; }

    /// <summary>Gets how many posts the selector judged and the filter could not, because the filter failed.</summary>
    public int FilterUndecided { get; init; }

    /// <summary>Gets the disagreements, filter-only first, then selector-only, then the pending posts when asked, each in
    /// post id order and together at most the request's limit.</summary>
    public IReadOnlyList<SelectorDiffItem> Items { get; init; } = [];
}

/// <summary>Represents one post the selector and the filter treat differently, or one the selector cannot judge yet.</summary>
public sealed record SelectorDiffItem
{
    /// <summary>Gets the post.</summary>
    public required Guid PostId { get; init; }

    /// <summary>Gets the source of the post's context link as <c>{family}/{handle}</c>, or null when unknown.</summary>
    public string? Source { get; init; }

    /// <summary>Gets the post's title with contacts replaced, cut to 140 characters.</summary>
    public string? Title { get; init; }

    /// <summary>Gets the English title of the Layer 1 answer, scrubbed and cut the same way, or null.</summary>
    public string? TitleEn { get; init; }

    /// <summary>Gets the filter's verdict: <c>yes</c>, <c>no</c> or <c>undecided</c>.</summary>
    public required string Filter { get; init; }

    /// <summary>Gets the selector's verdict.</summary>
    public required SelectorVerdict Selector { get; init; }

    /// <summary>Gets the clauses behind the selector's verdict.</summary>
    public IReadOnlyList<string> Clauses { get; init; } = [];

    /// <summary>Gets the entity type the Layer 1 answer gave, or null when it gave none.</summary>
    public string? EntityType { get; init; }

    /// <summary>Gets the post's most specific domain nodes.</summary>
    public IReadOnlyList<string> Domains { get; init; } = [];

    /// <summary>Gets the post's topics.</summary>
    public IReadOnlyList<string> Topics { get; init; } = [];

    /// <summary>Gets the timeframe the Layer 1 answer gave, or null when it gave none.</summary>
    public string? Timeframe { get; init; }
}

/// <summary>Represents what a selector says about one post's current revision and what its log holds of the post.</summary>
public sealed record SelectorExplainModel
{
    /// <summary>Gets the post.</summary>
    public required Guid PostId { get; init; }

    /// <summary>Gets the revision judged: the post's current one.</summary>
    public required int Revision { get; init; }

    /// <summary>Gets what the subscription's log holds of the post, from its newest content record: <c>none</c> when it
    /// holds none, <c>held</c> when that record delivers the post, <c>unmatched</c> when it is <c>post.unmatched</c>.</summary>
    public required string Held { get; init; }

    /// <summary>Gets the selector's verdict on the current revision.</summary>
    public required SelectorVerdict Verdict { get; init; }

    /// <summary>Gets the clauses behind the verdict.</summary>
    public IReadOnlyList<string> Clauses { get; init; } = [];

    /// <summary>Gets the merged context an event of this revision would carry in its payload.</summary>
    public IReadOnlyDictionary<string, string> Context { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets the entity type the Layer 1 answer gave, or null when it gave none.</summary>
    public string? EntityType { get; init; }

    /// <summary>Gets the post's most specific domain nodes.</summary>
    public IReadOnlyList<string> Domains { get; init; } = [];

    /// <summary>Gets the post's topics.</summary>
    public IReadOnlyList<string> Topics { get; init; } = [];
}

/// <summary>Represents a request to carry the posts other subscriptions delivered into a selector subscription's log.</summary>
public sealed record SelectorAdoptRequest
{
    /// <summary>Gets the active selector subscription that adopts the posts.</summary>
    public required Guid IntoSubscriptionId { get; init; }

    /// <summary>Gets the subscriptions whose delivered posts are adopted; each belongs to the same tenant and container as
    /// the adopter, and none is the adopter.</summary>
    public required IReadOnlyList<Guid> FromSubscriptionIds { get; init; }

    /// <summary>Gets whether to count what would be written and roll it back.</summary>
    public bool DryRun { get; init; }
}

/// <summary>Represents what an adoption considered and wrote.</summary>
/// <remarks>A second run writes nothing: a post the adopter has any record of is counted in <see cref="AlreadyHeld"/>, and a
/// pending post is counted again without a write.</remarks>
public sealed record SelectorAdoptModel
{
    /// <summary>Gets how many posts some source subscription currently holds: its newest content record of the post is not
    /// <c>post.unmatched</c>, and the post is live: neither gone, merged nor purged. A post that is no longer live was
    /// withdrawn from the older subscription already, so the selector never learns it as new.</summary>
    public int Considered { get; init; }

    /// <summary>Gets how many of those the adopter's log already holds a record of; they are left as they are.</summary>
    public int AlreadyHeld { get; init; }

    /// <summary>Gets how many the selector refuses: each got a <c>post.unmatched</c> so a consumer drops the post.</summary>
    public int Unmatched { get; init; }

    /// <summary>Gets how many the selector passes: each got a <c>post.new</c>.</summary>
    public int New { get; init; }

    /// <summary>Gets how many the selector cannot judge yet; they get nothing until their tags arrive.</summary>
    public int Pending { get; init; }

    /// <summary>Gets whether the counts come from a dry run that was rolled back.</summary>
    public bool DryRun { get; init; }
}
