using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;

/// <summary>Queues the extractions a selector's new pairs call for, after the selector subscription was created or changed.</summary>
/// <remarks>It refreshes <see cref="SelectorPostExtractionDemand"/> when that is the registered demand, then, when the host
/// composes <see cref="IPostExtractionBroker"/>, asks it to queue the stored posts whose done tags match a wanted pair and
/// have no done answer: the posts since the change's window, or every post for a new subscription. A host without
/// extraction leaves the hook idle; a refused backfill is logged and never fails the selector write.</remarks>
internal sealed class SelectorExtractionHook(IServiceProvider services, ILogger<SelectorExtractionHook> logger) : ISelectorChangeHook
{
    private const int BackfillLimit = 1000;

    /// <inheritdoc />
    public async Task OnChangedAsync(Guid subscriptionId, DateTimeOffset? windowSinceUtc, CancellationToken cancellationToken)
    {
        if (services.GetService<IPostExtractionDemand>() is SelectorPostExtractionDemand demand)
            demand.Invalidate();
        if (services.GetService<IPostExtractionBroker>() is not { } broker)
            return;

        var result = await broker.BackfillAsync(new PostExtractionBackfillRequest { SinceUtc = windowSinceUtc, Limit = BackfillLimit }, cancellationToken);
        if (result is Result<int>.Success { Value: var queued })
            logger.LogInformation("Selector subscription {SubscriptionId} queued {Count} extractions for its pairs", subscriptionId, queued);
        else
            logger.LogDebug("Selector subscription {SubscriptionId} queued no extractions: {Reason}", subscriptionId, ((Result<int>.Failure)result).Error.Message);
    }
}
