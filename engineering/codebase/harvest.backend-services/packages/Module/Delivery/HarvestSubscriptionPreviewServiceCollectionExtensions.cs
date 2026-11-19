using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;

/// <summary>Registers the module's selector preview.</summary>
public static class HarvestSubscriptionPreviewServiceCollectionExtensions
{
    /// <summary>Adds <see cref="ISubscriptionPreviewBroker"/> to an existing Harvest module: it counts what a selector would
    /// deliver over a container's live posts and writes nothing.</summary>
    public static IServiceCollection AddHarvestSubscriptionPreview(this IServiceCollection services)
    {
        services.TryAddScoped<ISubscriptionPreviewBroker, ModuleSubscriptionPreviewBroker>();
        return services;
    }
}
