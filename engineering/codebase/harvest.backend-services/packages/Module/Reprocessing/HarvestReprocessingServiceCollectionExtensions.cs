using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Reprocessing;

/// <summary>Registers the operator controls of the AI passes over an existing Harvest module with its post store.</summary>
public static class HarvestReprocessingServiceCollectionExtensions
{
    /// <summary>Adds <see cref="IPostReprocessingBroker"/>: re-runs, their status and eval sets from stored posts.</summary>
    /// <remarks>
    ///   - a re-run needs the shared AI module (<c>AddAi</c>); one that names <c>post-tags</c> needs tagging on as well
    ///   - the rows it queues are drained by the passes' own workers (<c>AddHarvestPostTagsWorker</c>,
    ///     <c>AddHarvestPostExtractionWorker</c>, <c>AddHarvestEntitiesWorker</c>)
    ///   - eval sets need the AI module's eval bench, which resolves the items through the <c>harvest:post:</c> subject
    ///     source <c>AddHarvestPostTags</c> registers
    /// </remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddHarvestPostReprocessing(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHarvestPosts();
        services.AddOptions();
        services.TryAddScoped<IPostReprocessingBroker, PostReprocessingBroker>();
        return services;
    }
}
