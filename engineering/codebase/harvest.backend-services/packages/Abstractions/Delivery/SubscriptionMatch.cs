namespace WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

/// <summary>Represents the verdict a selector event carries: which selector version judged the revision, what it said and
/// the clauses that decided it.</summary>
/// <remarks>Clauses read <c>type:event</c>, <c>domain:{key}</c>, <c>topic:{term}</c>, <c>sources</c>, or the reason a post
/// did not match: <c>except:{key}</c>, <c>exclude-domain:{key}</c>, <c>exclude-topic:{term}</c>,
/// <c>exclude-timeframe:{value}</c>, <c>no-type:{type}</c>, <c>no-source</c>, <c>no-schema</c>, <c>no-subject</c>, or for a
/// pending post <c>untagged</c> and <c>unsure-type</c>.</remarks>
public sealed record SubscriptionMatch
{
    /// <summary>Gets the version of the subscription's selector that judged the revision.</summary>
    public required int SelectorVersion { get; init; }

    /// <summary>Gets what the selector said.</summary>
    public required SelectorVerdict Verdict { get; init; }

    /// <summary>Gets the clauses behind the verdict, in evaluation order.</summary>
    public IReadOnlyList<string> Clauses { get; init; } = [];
}
