namespace WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

/// <summary>Represents what a selector subscription follows: Layer 1 tags and sources over one container, as one fixed
/// boolean form, <c>entityTypes ∧ (domains ∨ topics) ∧ ¬excludeDomains ∧ ¬excludeTopics ∧ ¬excludeTimeframes ∧ sources</c>.</summary>
/// <remarks>A client edits this, never a regex or a host filter. <see cref="SelectorRules"/> validates and normalizes it;
/// <see cref="SubscriptionSelectorMatcher"/> judges a post against it; <c>contract.md</c> § Subscription selectors holds the
/// wire and the evaluation order.</remarks>
public sealed record SubscriptionSelector
{
    /// <summary>Gets the entity types a post must carry (<c>event</c>, <c>listing</c>); empty means no type clause.</summary>
    public IReadOnlyList<string> EntityTypes { get; init; } = [];

    /// <summary>Gets the domain includes, any of which admits a post (OR with <see cref="Topics"/>).</summary>
    public IReadOnlyList<SelectorDomain> Domains { get; init; } = [];

    /// <summary>Gets the topic terms, matched as whole hyphen tokens, any of which admits a post (OR with
    /// <see cref="Domains"/>).</summary>
    public IReadOnlyList<string> Topics { get; init; } = [];

    /// <summary>Gets the domain keys that veto a post tagged at or under them.</summary>
    public IReadOnlyList<string> ExcludeDomains { get; init; } = [];

    /// <summary>Gets the topic terms that veto a post carrying a topic that matches them.</summary>
    public IReadOnlyList<string> ExcludeTopics { get; init; } = [];

    /// <summary>Gets the timeframes (<c>upcoming</c>, <c>ongoing</c>, <c>past</c>, <c>evergreen</c>, <c>unsure</c>) that
    /// veto a post whose <c>timeframe:</c> tag names one. A post without that tag is not vetoed, and never pending.</summary>
    public IReadOnlyList<string> ExcludeTimeframes { get; init; } = [];

    /// <summary>Gets the sources the post must come from; empty includes mean the whole container.</summary>
    public SelectorSources Sources { get; init; } = new();
}

/// <summary>Represents one domain include: a taxonomy node and the descendants that do not count.</summary>
public sealed record SelectorDomain
{
    /// <summary>Gets the taxonomy key, such as <c>style-fashion</c> or <c>business-and-finance/industries/apparel-industry</c>;
    /// a post tagged at or under it matches.</summary>
    public required string Key { get; init; }

    /// <summary>Gets the descendant keys, each strictly under <see cref="Key"/>, whose posts do not match through this
    /// include. A post that also sits under another node of the include still matches.</summary>
    public IReadOnlyList<string> Except { get; init; } = [];
}

/// <summary>Represents the source clause of a selector: which links of a post count.</summary>
public sealed record SelectorSources
{
    /// <summary>Gets the refs a link must match; empty means every link of the container.</summary>
    public IReadOnlyList<SelectorSourceRef> Include { get; init; } = [];

    /// <summary>Gets the refs that remove a link; an excluded link never counts, even when an include matches it.</summary>
    public IReadOnlyList<SelectorSourceRef> Exclude { get; init; } = [];
}

/// <summary>Represents a reference to a source group, a source or a section (pipeline); exactly one id is set.</summary>
public sealed record SelectorSourceRef
{
    /// <summary>Gets the source group the link's source belongs to.</summary>
    public Guid? GroupId { get; init; }

    /// <summary>Gets the source the link belongs to.</summary>
    public Guid? SourceId { get; init; }

    /// <summary>Gets the pipeline whose source and path scope the link must fall in.</summary>
    public Guid? PipelineId { get; init; }

    /// <summary>Gets the opaque defaults an include ref adds to the events of the links it matches, such as
    /// <c>{"defaultKind":"trade-fair"}</c>; an exclude ref carries none.</summary>
    public IReadOnlyDictionary<string, string> Context { get; init; } = new Dictionary<string, string>();
}
