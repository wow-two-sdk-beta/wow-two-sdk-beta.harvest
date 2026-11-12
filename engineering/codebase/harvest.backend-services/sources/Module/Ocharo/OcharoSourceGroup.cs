namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;

/// <summary>One publisher or operator. Shared hosting does not establish shared ownership.</summary>
public sealed record OcharoSourceGroup
{
    /// <summary>The stable publisher ID from the source inventory.</summary>
    public required string Id { get; init; }

    /// <summary>The reviewed publisher name.</summary>
    public required string Name { get; init; }

    /// <summary>The shortlist order for Ocharo, independent of automation readiness.</summary>
    public required int Rank { get; init; }

    /// <summary>The Ocharo priority tier.</summary>
    public required string Tier { get; init; }

    /// <summary>The publisher's evidence role, such as organizer, venue or ticket seller.</summary>
    public required string SourceType { get; init; }

    /// <summary>The reviewed geographic and subject coverage.</summary>
    public required string Coverage { get; init; }

    /// <summary>Why the publisher belongs in the shortlist.</summary>
    public required string Rationale { get; init; }

    /// <summary>Known recurring event families; these do not imply future dates.</summary>
    public required IReadOnlyList<string> EventFamilies { get; init; }

    /// <summary>Known upstream publisher IDs for an aggregator. They keep independent identities.</summary>
    public required IReadOnlyList<string> Aggregates { get; init; }

    /// <summary>The group's owned, related, cross-check and reference channels.</summary>
    public required IReadOnlyList<OcharoSourceChannel> Channels { get; init; }
}
