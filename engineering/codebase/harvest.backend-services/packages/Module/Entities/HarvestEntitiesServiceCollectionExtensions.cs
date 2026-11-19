using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Entities;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;

/// <summary>Registers the entity layer over an existing Harvest module.</summary>
public static class HarvestEntitiesServiceCollectionExtensions
{
    /// <summary>Adds the read contract <see cref="IEntitiesBroker"/> — search, an entity's years and posts, the calendar
    /// and the change feed — over the entity layer's tables; it needs no options and reads nothing while the layer is
    /// off.</summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddHarvestEntityReads(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddScoped<IEntitiesBroker, ModuleEntitiesBroker>();
        return services;
    }

    /// <summary>Adds <see cref="IEntityBroker"/> and the reads of <see cref="AddHarvestEntityReads"/>; starts no work. A
    /// done extraction queues resolution only while <see cref="EntityOptions.Enabled"/> is set.</summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The options builder, through which the host binds <see cref="EntityConstants.SectionName"/>.</returns>
    public static OptionsBuilder<EntityOptions> AddHarvestEntities(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton<EntityGate>();
        services.TryAddScoped<IEntityBroker, EntityBroker>();
        services.AddHarvestEntityReads();
        return services.AddOptions<EntityOptions>()
            .Validate(options => options.CandidateFloor is > 0 and <= 1 && options.MinMatchConfidence is > 0 and <= 1
                && options.CandidateFloor <= options.MinMatchConfidence,
                "Harvest:Entities needs 0 < CandidateFloor <= MinMatchConfidence <= 1.")
            .Validate(options => options.AmbiguityMargin is >= 0 and < 0.5, "Harvest:Entities:AmbiguityMargin must be 0 to 0.5.")
            .Validate(options => options.EventDateWindowDays is >= 0 and <= 60 && options.UpdateWindowDays is >= 1 and <= 1000,
                "Harvest:Entities:EventDateWindowDays must be 0 to 60 and UpdateWindowDays 1 to 1,000.")
            .Validate(options => options.ListingAreaTolerance is >= 0 and <= 0.5 && options.ListingPriceBand is >= 0 and <= 1,
                "Harvest:Entities:ListingAreaTolerance must be 0 to 0.5 and ListingPriceBand 0 to 1.")
            .Validate(options => options.Cadence is { AnnualDays: >= 1 and <= 120, SemiannualDays: >= 1 and <= 60, QuarterlyDays: >= 1 and <= 30,
                    MonthlyDays: >= 1 and <= 10, PredictMinConfidence: >= 0 and <= 1 },
                "Harvest:Entities:Cadence tolerances must be 1-120 (annual), 1-60, 1-30 and 1-10 days; PredictMinConfidence 0 to 1.")
            .Validate(options => options.BatchSize is >= 1 and <= 100, "Harvest:Entities:BatchSize must be 1 to 100.")
            .Validate(options => options.PollInterval > TimeSpan.Zero && options.RetryDelay > TimeSpan.Zero
                && options.LeaseDuration >= TimeSpan.FromSeconds(10), "Harvest:Entities intervals must be positive; a lease lasts 10 seconds or more.");
    }

    /// <summary>Opts into the background worker that drains the resolution queue; it idles unless the layer is enabled and
    /// the engine worker (<c>Harvest:Worker:Enabled</c>) is enabled.</summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddHarvestEntitiesWorker(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddHostedService<EntityWorker>();
        return services;
    }
}

/// <summary>Answers whether a done extraction queues its post's resolution in this host.</summary>
internal sealed class EntityGate(IOptionsMonitor<EntityOptions> options)
{
    /// <summary>Gets whether the entity layer is enabled.</summary>
    internal bool Enabled => options.CurrentValue.Enabled;
}
