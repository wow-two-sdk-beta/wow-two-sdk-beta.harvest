namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;

/// <summary>The durable identifiers needed to run and pull one Ocharo source pipeline.</summary>
/// <param name="SourceKey">The named adapter.</param>
/// <param name="Seed">The reviewed listing URL.</param>
/// <param name="SourceId">The registered source.</param>
/// <param name="PipelineId">The standing pipeline.</param>
/// <param name="SubscriptionId">The filtered, resumable pull subscription.</param>
public sealed record OcharoSourceRegistration(string SourceKey, string Seed, Guid SourceId, Guid PipelineId, Guid SubscriptionId)
{
    /// <summary>The publisher group, distinct from the reader or physical host.</summary>
    public required string GroupId { get; init; }

    /// <summary>The channel within the publisher group.</summary>
    public required string ChannelId { get; init; }

    /// <summary>The recurring event family if the channel represents a single family.</summary>
    public string? EventFamily { get; init; }
}
