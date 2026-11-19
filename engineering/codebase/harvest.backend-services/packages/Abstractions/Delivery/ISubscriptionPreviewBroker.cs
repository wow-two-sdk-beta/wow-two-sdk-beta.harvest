using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

/// <summary>Counts what a selector would deliver over a window of one container's posts, before it is saved.</summary>
public interface ISubscriptionPreviewBroker
{
    /// <summary>Judges the container's live posts that changed since the window's start and reports counts per verdict, per
    /// source and per clause, with sample matches and near misses. Writes nothing.</summary>
    Task<Result<SubscriptionPreviewModel>> PreviewAsync(SubscriptionPreviewRequest request, CancellationToken cancellationToken = default);
}
