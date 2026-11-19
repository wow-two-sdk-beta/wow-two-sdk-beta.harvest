using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

/// <summary>Registers pull subscriptions and reads their durable, resumable event logs.</summary>
public interface IDeliveryBroker
{
    /// <summary>Subscribes a tenant to an accessible pipeline, or with a selector to Layer 1 tags and sources over one
    /// container, optionally backfilling existing live posts.</summary>
    Task<Result<SubscriptionModel>> SubscribeAsync(SubscriptionDeclaration declaration, CancellationToken cancellationToken = default);
    /// <summary>Reads subscription metadata.</summary>
    Task<Result<SubscriptionModel>> GetSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default);
    /// <summary>Lists all subscriptions, or only a specified tenant's subscriptions.</summary>
    Task<Result<IReadOnlyList<SubscriptionModel>>> ListSubscriptionsAsync(Guid? tenantId = null, CancellationToken cancellationToken = default);
    /// <summary>Reads committed events below the transaction snapshot horizon; old cursors replay the same events.</summary>
    Task<Result<CursorPage<DeliveryEvent>>> PullAsync(Guid subscriptionId, string? cursor = null, int limit = 100, CancellationToken cancellationToken = default);
    /// <summary>Replaces a selector subscription's selector and re-judges its window on the same cursor: new matches arrive
    /// as <c>post.new</c>, posts that no longer match as <c>post.unmatched</c>, and posts that match again as
    /// <c>post.changed</c>. A selector equal to the stored one writes nothing.</summary>
    Task<Result<SelectorChangeModel>> SetSelectorAsync(Guid subscriptionId, SelectorChange change, CancellationToken cancellationToken = default);
    /// <summary>Moves a subscription between <c>active</c> and <c>suspended</c>, or to <c>archived</c>, which is final.</summary>
    Task<Result<SubscriptionModel>> SetStateAsync(Guid subscriptionId, SubscriptionState state, CancellationToken cancellationToken = default);
}
