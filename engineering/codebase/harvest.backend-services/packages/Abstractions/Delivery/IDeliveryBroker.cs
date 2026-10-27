using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

/// <summary>Registers pull subscriptions and reads their durable, resumable event logs.</summary>
public interface IDeliveryBroker
{
    /// <summary>Subscribes a tenant to an accessible pipeline, optionally backfilling existing live posts.</summary>
    Task<Result<SubscriptionModel>> SubscribeAsync(SubscriptionDeclaration declaration, CancellationToken cancellationToken = default);
    /// <summary>Reads subscription metadata.</summary>
    Task<Result<SubscriptionModel>> GetSubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken = default);
    /// <summary>Lists all subscriptions, or only a specified tenant's subscriptions.</summary>
    Task<Result<IReadOnlyList<SubscriptionModel>>> ListSubscriptionsAsync(Guid? tenantId = null, CancellationToken cancellationToken = default);
    /// <summary>Reads committed events below the transaction snapshot horizon; old cursors replay the same events.</summary>
    Task<Result<CursorPage<DeliveryEvent>>> PullAsync(Guid subscriptionId, string? cursor = null, int limit = 100, CancellationToken cancellationToken = default);
}
