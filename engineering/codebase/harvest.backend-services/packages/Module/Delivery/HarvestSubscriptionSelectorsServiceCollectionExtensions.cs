using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;

/// <summary>Registers what a host needs to cut a consumer over to a selector subscription.</summary>
public static class HarvestSubscriptionSelectorsServiceCollectionExtensions
{
    /// <summary>Adds <see cref="ISubscriptionOperationsBroker"/> to an existing Harvest module: the diff against a filter, the
    /// explanation of one post's verdict and the adoption of older subscriptions' posts.</summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddHarvestSubscriptionOperations(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<ISubscriptionOperationsBroker, SubscriptionOperationsBroker>();
        return services;
    }

    /// <summary>Makes the active selector subscriptions the source of Layer 2 extraction demand, beside the configured pairs.</summary>
    /// <remarks>
    ///   - it replaces <see cref="IPostExtractionDemand"/> with <see cref="SelectorPostExtractionDemand"/>, whichever of this
    ///     call and <c>AddHarvestPostExtraction</c> runs first, because the module's default is registered only when none is
    ///   - it adds the hook that queues the extractions a created or changed selector's new pairs call for
    /// </remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddHarvestSelectorDemand(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddOptions();
        services.Replace(ServiceDescriptor.Singleton<IPostExtractionDemand, SelectorPostExtractionDemand>());
        services.TryAddEnumerable(ServiceDescriptor.Scoped<ISelectorChangeHook, SelectorExtractionHook>());
        return services;
    }
}
