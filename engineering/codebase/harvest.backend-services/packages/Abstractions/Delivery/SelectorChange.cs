namespace WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

/// <summary>Declares a new selector for a selector subscription.</summary>
public sealed record SelectorChange
{
    /// <summary>Gets the new selector; one equal to the stored selector changes nothing.</summary>
    public required SubscriptionSelector Selector { get; init; }

    /// <summary>Gets how far back live posts that changed are re-judged, defaulting to
    /// <see cref="SelectorRules.DefaultWindowDays"/> days and at most <see cref="SelectorRules.MaxWindowDays"/>; posts the
    /// subscription already has a record of are re-judged whatever their age.</summary>
    public DateTimeOffset? WindowSinceUtc { get; init; }

    /// <summary>Gets who changed it, kept in the selector's history; a host sets it from the caller.</summary>
    public string? ChangedBy { get; init; }
}

/// <summary>Represents what a selector change did: the subscription as it stands and what the change window wrote.</summary>
public sealed record SelectorChangeModel
{
    /// <summary>Gets the subscription after the change.</summary>
    public required SubscriptionModel Subscription { get; init; }

    /// <summary>Gets whether the selector differed from the stored one; false means nothing was written.</summary>
    public required bool Changed { get; init; }

    /// <summary>Gets the lower bound of the window that was re-judged; null when nothing changed.</summary>
    public DateTimeOffset? WindowSinceUtc { get; init; }

    /// <summary>Gets how many posts the window re-judged.</summary>
    public int Considered { get; init; }

    /// <summary>Gets how many posts entered the log as <c>post.new</c>.</summary>
    public int NewMatches { get; init; }

    /// <summary>Gets how many held posts left it as <c>post.unmatched</c>.</summary>
    public int Unmatched { get; init; }

    /// <summary>Gets how many unmatched posts came back as <c>post.changed</c>.</summary>
    public int Rematched { get; init; }

    /// <summary>Gets how many posts the selector could not judge yet.</summary>
    public int Pending { get; init; }
}

/// <summary>Declares a new state for a subscription.</summary>
public sealed record SubscriptionStateChange
{
    /// <summary>Gets the state to move to: <c>active</c> and <c>suspended</c> swap, and <c>archived</c> is final.</summary>
    public required SubscriptionState State { get; init; }
}
