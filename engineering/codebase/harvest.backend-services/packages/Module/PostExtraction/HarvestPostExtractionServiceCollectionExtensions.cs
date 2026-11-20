using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Budget;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

/// <summary>Registers the Layer 2 extraction over an existing Harvest module with its post store.</summary>
public static class HarvestPostExtractionServiceCollectionExtensions
{
    /// <summary>Adds <see cref="IPostExtractionBroker"/> and the configured demand gate; starts no work.</summary>
    /// <remarks>
    ///   - a done <c>post-tags</c> answer queues extraction only when the host composes the shared AI module (<c>AddAi</c>)
    ///     and <see cref="IPostExtractionDemand"/> wants its (entity type, domain) pair
    ///   - the default demand reads <see cref="PostExtractionOptions.Wanted"/>; a subscription selector registered before
    ///     this call replaces it
    ///   - <see cref="PostExtractionOptions.Models"/> falls back to <see cref="PostExtractionConstants.DefaultModel"/>; a host
    ///     that filters routes adds its own post-configuration after this call
    /// </remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The options builder, through which the host binds <see cref="PostExtractionConstants.SectionName"/>.</returns>
    public static OptionsBuilder<PostExtractionOptions> AddHarvestPostExtraction(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHarvestPosts();
        services.AddHarvestProcessingBudget();
        services.TryAddSingleton<IPostExtractionDemand, ConfiguredPostExtractionDemand>();
        services.TryAddScoped<IPostExtractionBroker, PostExtractionBroker>();
        return services.AddOptions<PostExtractionOptions>()
            .PostConfigure(options =>
            {
                if (options.Models.Length == 0)
                    options.Models = [PostExtractionConstants.DefaultModel];
            })
            .Validate(options => options.Wanted.All(PostExtractionDemand.IsValid),
                "Harvest:Processing:Extraction:Wanted takes { entityType: event | listing, domain: a taxonomy key } pairs.")
            .Validate(options => options.PromptVersions.All(pin => PostExtractionConstants.UseCases.Contains(pin.Key) && pin.Value > 0),
                "Harvest:Processing:Extraction:PromptVersions pins a positive version per extract-event or extract-listing.")
            .Validate(options => options.MaxTextChars is >= 200 and <= 100_000, "Harvest:Processing:Extraction:MaxTextChars must be 200 to 100,000.")
            .Validate(options => options.BatchSize is >= 1 and <= 100, "Harvest:Processing:Extraction:BatchSize must be 1 to 100.")
            .Validate(options => options.PollInterval > TimeSpan.Zero && options.RetryDelay > TimeSpan.Zero
                && options.LeaseDuration >= TimeSpan.FromSeconds(10), "Harvest:Processing:Extraction intervals must be positive; a lease lasts 10 seconds or more.");
    }

    /// <summary>Opts into the background worker that drains the extraction queue; it idles unless the host extracts and
    /// the engine worker (<c>Harvest:Worker:Enabled</c>) is enabled.</summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddHarvestPostExtractionWorker(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHostedService<PostExtractionWorker>();
        return services;
    }
}
