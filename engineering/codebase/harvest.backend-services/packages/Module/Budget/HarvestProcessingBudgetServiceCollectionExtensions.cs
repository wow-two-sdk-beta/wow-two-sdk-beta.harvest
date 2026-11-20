using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Budget;

/// <summary>Registers the AI spend guard over an existing Harvest module.</summary>
public static class HarvestProcessingBudgetServiceCollectionExtensions
{
    /// <summary>Adds <see cref="IProcessingBudget"/>, a singleton that reads the AI module's inference log; starts no work.</summary>
    /// <remarks>
    ///   - the post-tags, extraction and re-run registrations call it, so a host binds the settings once, here
    ///   - with no <see cref="ProcessingBudgetOptions.LimitUsd"/>, or with no inference log composed (<c>AddAi</c>), the
    ///     state is <see cref="BudgetState.Off"/> and nothing changes
    ///   - a repeated call adds no second set of validation rules
    /// </remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The options builder, through which the host binds <see cref="ProcessingBudgetConstants.SectionName"/>.</returns>
    public static OptionsBuilder<ProcessingBudgetOptions> AddHarvestProcessingBudget(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        bool first = !services.Any(descriptor => descriptor.ServiceType == typeof(IProcessingBudget));
        services.TryAddSingleton<IProcessingBudget, ProcessingBudget>();
        var builder = services.AddOptions<ProcessingBudgetOptions>();
        if (first)
            builder
                .Validate(options => options.LimitUsd is null or > 0 and <= 1_000_000_000m,
                    "Harvest:Processing:Budget:LimitUsd must be above 0 and at most 1,000,000,000.")
                .Validate(options => options.SoftShare is > 0 and <= 1, "Harvest:Processing:Budget:SoftShare must be above 0 and at most 1.")
                .Validate(options => options.CycleStartDay is >= 1 and <= 28, "Harvest:Processing:Budget:CycleStartDay must be 1 to 28.")
                .Validate(options => options.RefreshInterval > TimeSpan.Zero && options.RefreshInterval <= TimeSpan.FromHours(1),
                    "Harvest:Processing:Budget:RefreshInterval must be positive and at most one hour.");
        return builder;
    }
}
