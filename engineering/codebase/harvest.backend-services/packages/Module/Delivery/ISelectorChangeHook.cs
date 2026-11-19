namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;

/// <summary>Reacts to a selector subscription that was created or whose selector changed, after the change committed.</summary>
/// <remarks>The delivery broker calls each registered hook once the transaction is durable, so a hook sees the stored
/// selector. A hook's failure is logged and never fails the write; a hook that needs to retry keeps its own state.</remarks>
internal interface ISelectorChangeHook
{
    /// <summary>Runs after a selector subscription was created or its selector changed.</summary>
    /// <param name="subscriptionId">The selector subscription.</param>
    /// <param name="windowSinceUtc">The lower bound of the posts the change re-judged, or null when a new subscription
    /// backfilled every live post.</param>
    /// <param name="cancellationToken">The caller's token.</param>
    Task OnChangedAsync(Guid subscriptionId, DateTimeOffset? windowSinceUtc, CancellationToken cancellationToken);
}
