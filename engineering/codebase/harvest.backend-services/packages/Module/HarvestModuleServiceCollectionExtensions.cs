using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Data.EntityFrameworkCore.Sqlite;
using WoW.Two.Sdk.Beta.Be.Observability.HealthChecks;
using WoW.Two.Sdk.Beta.Be.Harvest.Greetings;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Greetings;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module;

/// <summary>Registers the in-process Harvest module (Form 1).</summary>
public static class HarvestModuleServiceCollectionExtensions
{
    /// <summary>Registers <see cref="IGreetingsBroker"/> over the module's store, and the store's readiness check.</summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The options builder, through which the host sets the connection string; it is read on first use.</returns>
    public static OptionsBuilder<HarvestModuleOptions> AddHarvest(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddDbContext<HarvestDbContext>((provider, options) =>
            options.UseSqliteConventional(provider.GetRequiredService<IOptions<HarvestModuleOptions>>().Value.ConnectionString));
        services.AddScoped<IGreetingsBroker, ModuleGreetingsBroker>();
        services.AddHealthChecks().AddDatabaseReadinessCheck<HarvestDbContext>();

        return services.AddOptions<HarvestModuleOptions>()
            .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), "HarvestModuleOptions.ConnectionString must be set.");
    }
}
