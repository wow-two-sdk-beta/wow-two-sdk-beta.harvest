namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;

/// <summary>A publisher channel, access route or corroborating link, with its own collection decision.</summary>
public sealed record OcharoSourceChannel
{
    /// <summary>The stable channel ID within its publisher group.</summary>
    public required string Id { get; init; }

    /// <summary>The public URL cited by the source review.</summary>
    public required string Url { get; init; }

    /// <summary>The channel's human-readable role, such as market announcements or stand enquiry.</summary>
    public required string Role { get; init; }

    /// <summary>Whether the publisher owns the channel, is related to it, uses it as a cross-check or links an access route.</summary>
    public required string Relationship { get; init; }

    /// <summary>Marks the starting URL for researching this group. It need not be a collectible feed.</summary>
    public bool Primary { get; init; }

    /// <summary>Harvest, provider opt-in, planned reader, manual review or reference.</summary>
    public required string Mode { get; init; }

    /// <summary>The intended collection method, independent of whether a reader is ready.</summary>
    public required string Method { get; init; }

    /// <summary>Source-specific collection and interpretation notes.</summary>
    public required string Plan { get; init; }

    /// <summary>Observed page accessibility during the source review, when individually checked.</summary>
    public string? CheckState { get; init; }

    /// <summary>Additional channel context from the source review.</summary>
    public string? Note { get; init; }

    /// <summary>The event family announced on this channel, when it represents one known family.</summary>
    public string? EventFamily { get; init; }

    /// <summary>The other publisher, when a related or corroborating URL is also indexed there.</summary>
    public string? RelatedGroupId { get; init; }

    /// <summary>The installed reader key for a harvest or provider channel.</summary>
    public string? AdapterKey { get; init; }

    /// <summary>Whether default setup includes the channel or it needs explicit group selection.</summary>
    public string? Setup { get; init; }

    /// <summary>A distinct runtime URL for an equivalent channel when the adapter requires a canonical profile URL.</summary>
    public string? Seed { get; init; }

    internal string PipelineUrl => Seed ?? Url;
}
