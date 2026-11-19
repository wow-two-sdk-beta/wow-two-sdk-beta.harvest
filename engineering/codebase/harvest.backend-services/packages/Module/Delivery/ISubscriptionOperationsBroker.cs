using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;

/// <summary>Operates a selector subscription's cutover from older subscriptions: compares it with a filter, explains one
/// post's verdict, and adopts the posts the older subscriptions delivered.</summary>
/// <remarks>The module's own surface, composed by hosts that run the <c>subscriptions</c> verb; it is not part of the
/// delivery contract a client reaches over HTTP.</remarks>
public interface ISubscriptionOperationsBroker
{
    /// <summary>Compares the selector's current verdicts with a registered filter's over the container's recent posts;
    /// writes nothing.</summary>
    /// <param name="request">Which subscription, which filter and how much to list.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The counts and disagreements; a validation failure for an unregistered filter, a bad window or limit, a
    /// subscription that is not a selector or a window of more than 10,000 posts; not found for a missing subscription.</returns>
    Task<Result<SelectorDiffModel>> DiffAsync(SelectorDiffRequest request, CancellationToken cancellationToken = default);

    /// <summary>Judges one post's current revision against the selector and says what the subscription's log holds of it;
    /// writes nothing.</summary>
    /// <param name="subscriptionId">The selector subscription.</param>
    /// <param name="postId">A post of the subscription's container.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The verdict with its clauses and context; not found for a missing subscription or a post outside its
    /// container, a validation failure for a subscription that is not a selector.</returns>
    Task<Result<SelectorExplainModel>> ExplainAsync(Guid subscriptionId, Guid postId, CancellationToken cancellationToken = default);

    /// <summary>Writes into a selector subscription's log the posts other subscriptions of the same tenant and container
    /// hold: <c>post.unmatched</c> for those the selector refuses, <c>post.new</c> for those it passes.</summary>
    /// <param name="request">The adopter, the subscriptions to adopt from and whether it is a dry run.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>What was considered and written; a validation failure for a malformed or mixed set, a conflict when the
    /// adopter is not active, not found for a missing subscription.</returns>
    Task<Result<SelectorAdoptModel>> AdoptAsync(SelectorAdoptRequest request, CancellationToken cancellationToken = default);
}
