using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Ai.Evals;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Budget;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

/// <summary>Registers the Layer 1 post-tags pass over an existing Harvest module with its post store.</summary>
public static class HarvestPostTagsServiceCollectionExtensions
{
    /// <summary>Adds <see cref="IPostTagsBroker"/> and the <c>harvest:post:</c> eval subject source; starts no work.</summary>
    /// <remarks>
    ///   - post writes queue tagging only when <see cref="PostTagsOptions.Enabled"/> is set and the host composes the
    ///     shared AI module (<c>AddAi</c>, which supplies <c>IAiRunner</c>)
    ///   - <see cref="PostTagsOptions.Models"/> falls back to <see cref="PostTagsConstants.DefaultModel"/>; a host that
    ///     filters routes adds its own post-configuration after this call
    /// </remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The options builder, through which the host binds <see cref="PostTagsConstants.SectionName"/>.</returns>
    public static OptionsBuilder<PostTagsOptions> AddHarvestPostTags(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHarvestPosts();
        services.AddHarvestProcessingBudget();
        services.TryAddScoped<IPostTagsBroker, PostTagsBroker>();
        services.TryAddEnumerable(ServiceDescriptor.Scoped<IEvalSubjectSource, PostTagsSubjectSource>());
        return services.AddOptions<PostTagsOptions>()
            .PostConfigure(options =>
            {
                if (options.Models.Length == 0)
                    options.Models = [PostTagsConstants.DefaultModel];
            })
            .Validate(options => options.MaxTextChars is >= 200 and <= 100_000, "Harvest:Processing:Tags:MaxTextChars must be 200 to 100,000.")
            .Validate(options => options.BatchSize is >= 1 and <= 100, "Harvest:Processing:Tags:BatchSize must be 1 to 100.")
            .Validate(options => options.PromptVersion is null or > 0, "Harvest:Processing:Tags:PromptVersion must be positive.")
            .Validate(options => options.PollInterval > TimeSpan.Zero && options.RetryDelay > TimeSpan.Zero
                && options.LeaseDuration >= TimeSpan.FromSeconds(10), "Harvest:Processing:Tags intervals must be positive; a lease lasts 10 seconds or more.");
    }

    /// <summary>Opts into the background worker that drains the post-tags queue; it idles unless tagging is on and the
    /// engine worker (<c>Harvest:Worker:Enabled</c>) is enabled.</summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddHarvestPostTagsWorker(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHostedService<PostTagsWorker>();
        return services;
    }
}
