using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Marketplace;

/// <summary>One provisioned marketplace intake.</summary>
/// <param name="Profile">The applied profile's key.</param>
/// <param name="TenantId">The consumer's tenant.</param>
/// <param name="ContainerId">The tenant's private listing container.</param>
/// <param name="SourceId">The marketplace source the searches read.</param>
/// <param name="Searches">One pipeline and one pull subscription per saved search, in profile order.</param>
/// <param name="ConsumerKey">The consumer's newest active key, or null when it has none; its secret only when this call
/// issued it.</param>
public sealed record MarketplaceSetup(string Profile, Guid TenantId, Guid ContainerId, Guid SourceId,
    IReadOnlyList<MarketplaceSearchRegistration> Searches, MarketplaceConsumerKey? ConsumerKey);

/// <summary>One saved search's pipeline and subscription.</summary>
/// <param name="Key">The search's key in the profile.</param>
/// <param name="Url">The search URL the pipeline starts from.</param>
/// <param name="PipelineId">The search's pipeline.</param>
/// <param name="SubscriptionId">The <c>wow2.post.v1</c> subscription the consumer pulls.</param>
public sealed record MarketplaceSearchRegistration(string Key, string Url, Guid PipelineId, Guid SubscriptionId);

/// <summary>The consumer's tenant API key: <c>events:read</c> on its own tenant only.</summary>
/// <param name="Id">The key's ID, which revocation takes.</param>
/// <param name="Prefix">The key's public prefix, which identifies it in logs.</param>
/// <param name="Secret">The secret, present only in the result of the call that issued it; it is never readable again.</param>
public sealed record MarketplaceConsumerKey(Guid Id, string Prefix, string? Secret);

/// <summary>What a key action asks of setup.</summary>
public enum MarketplaceKeyAction
{
    /// <summary>Issue nothing; report the newest active consumer key.</summary>
    None,

    /// <summary>Issue a consumer key when the consumer holds no active one.</summary>
    Issue,

    /// <summary>Revoke every active consumer key and issue a new one.</summary>
    Rotate,
}

/// <summary>What setup owns on a target, read without writing.</summary>
/// <param name="Profile">The profile's key.</param>
/// <param name="TenantId">The consumer's tenant.</param>
/// <param name="ContainerId">The tenant's private listing container.</param>
/// <param name="Searches">One row per saved search, in profile order.</param>
/// <param name="ActiveKeys">How many active keys the consumer holds.</param>
public sealed record MarketplaceStatus(string Profile, Guid TenantId, Guid ContainerId, IReadOnlyList<MarketplaceSearchStatus> Searches,
    int ActiveKeys);

/// <summary>One saved search with its pipeline and subscription, or none when setup has not created them.</summary>
/// <param name="Key">The search's key in the profile.</param>
/// <param name="Pipeline">The search's pipeline, with its schedules.</param>
/// <param name="SubscriptionId">The search's listing subscription.</param>
public sealed record MarketplaceSearchStatus(string Key, PipelineModel? Pipeline, Guid? SubscriptionId);
