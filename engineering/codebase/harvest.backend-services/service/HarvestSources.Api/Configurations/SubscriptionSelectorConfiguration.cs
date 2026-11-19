using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;

namespace HarvestSources.Api.Configurations;

internal static class SubscriptionSelectorConfiguration
{
    /// <summary>Composes what a consumer's cutover to a selector subscription needs on the host: the operations broker behind the
    /// <c>subscriptions</c> verb (diff, explain, adopt), and the selectors as a source of Layer 2 extraction demand beside
    /// <c>Harvest:Processing:Extraction:Wanted</c>. Both are inert on a host without AI: the demand is read only by extraction,
    /// and the hook that queues a changed selector's extractions finds no extraction broker and returns.</summary>
    internal static void AddSubscriptionSelectors(this WebApplicationBuilder builder)
    {
        builder.Services.AddHarvestSubscriptionOperations();
        builder.Services.AddHarvestSelectorDemand();
    }
}
