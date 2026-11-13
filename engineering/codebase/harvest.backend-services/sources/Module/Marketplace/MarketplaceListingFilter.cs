using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Marketplace;

/// <summary>Delivers marketplace listings only: a search page, an article or an unrecognised post stays out of the stream.</summary>
/// <remarks>Lifecycle events of a post the subscription already received (<c>link.state</c>, merges) pass regardless, as
/// for every filter.</remarks>
public sealed class MarketplaceListingFilter : IDeliveryPostFilter
{
    /// <summary>The stable subscription filter key.</summary>
    public const string FilterKey = "marketplace-listing-v1";

    /// <inheritdoc />
    public string Key => FilterKey;

    /// <inheritdoc />
    public bool Matches(PostEnvelope envelope) => envelope.Kind == PostKind.Listing;
}
