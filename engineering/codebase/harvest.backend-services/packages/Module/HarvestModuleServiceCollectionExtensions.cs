using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Data.Abstractions;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.ApiKeys;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities.Challenges;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;
using WoW.Two.Sdk.Beta.Be.Observability.HealthChecks;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module;

/// <summary>Registers the in-process Harvest module (Form 1).</summary>
public static class HarvestModuleServiceCollectionExtensions
{
    /// <summary>Registers the module's store on its own keyed Npgsql data source, its migration set
    /// (<see cref="HarvestModuleConstants.MigrationSetName"/>, history in <c>harvest_migration_history</c>), the store's
    /// readiness check and <see cref="IRegistrationBroker"/>.</summary>
    /// <remarks>Nothing it registers is unkeyed infrastructure, so it sits beside a host's own database and migrator. Call
    /// <c>MigrateHarvestOnStartupAsync</c> before serving.</remarks>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The options builder, through which the host sets the connection string; it is read on first use.</returns>
    public static OptionsBuilder<HarvestModuleOptions> AddHarvest(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.TryAddSingleton(TimeProvider.System);
        services.AddKeyedSingleton(HarvestModuleConstants.ServiceKey, (provider, _) =>
            NpgsqlDataSource.Create(provider.GetRequiredService<IOptions<HarvestModuleOptions>>().Value.ConnectionString));
        // Keyed by the set's own name, so the migration set opens its connections here, never on the host's database.
        services.AddKeyedSingleton<IDbConnectionFactory>(HarvestModuleConstants.ServiceKey, (provider, _) =>
            new DataSourceConnectionFactory(provider.GetRequiredKeyedService<NpgsqlDataSource>(HarvestModuleConstants.ServiceKey)));
        services.AddDbContext<HarvestDbContext>((provider, options) => options
            .UseNpgsql(provider.GetRequiredKeyedService<NpgsqlDataSource>(HarvestModuleConstants.ServiceKey))
            .UseSnakeCaseNamingConvention());
        services.AddDatabaseBespokeMigrationSet(HarvestModuleConstants.MigrationSetName, typeof(HarvestModuleAssembly).Assembly, migrations =>
        {
            migrations.Provider = DatabaseProvider.Postgres;
            migrations.Version = MigrationVersionMapper.ToVersion(typeof(HarvestModuleAssembly).Assembly);
        });
        services.AddScoped<IRegistrationBroker, ModuleRegistrationBroker>();
        services.AddSingleton<IFetchClientFactory, FetchClientFactory>();
        services.AddSingleton<IChallengeDetector, GenericChallengeDetector>();
        services.AddHealthChecks().AddDatabaseReadinessCheck<HarvestDbContext>();

        return services.AddOptions<HarvestModuleOptions>()
            .Validate(options => !string.IsNullOrWhiteSpace(options.ConnectionString), "HarvestModuleOptions.ConnectionString must be set.")
            .Validate(
                options => string.IsNullOrEmpty(options.OperatorApiKey) || IsHarvestSecret(options.OperatorApiKey),
                $"HarvestModuleOptions.OperatorApiKey must be '{ApiKeyConstants.Marker}' followed by {ApiKeyConstants.RandomLength} letters and digits.");
    }

    /// <summary>Answers core's API key scheme from the module's key table and the operator key — for a host that serves
    /// Harvest's HTTP API with <c>AddApiKeyAuthentication</c>, marker <see cref="ApiKeyConstants.Marker"/>.</summary>
    /// <param name="services">The service collection to configure.</param>
    /// <returns>The same <paramref name="services"/> for chaining.</returns>
    public static IServiceCollection AddHarvestApiKeys(this IServiceCollection services)
    {
        ArgumentNullException.ThrowIfNull(services);

        services.AddScoped<IApiKeyRepository, ModuleApiKeyRepository>();
        return services;
    }

    private static bool IsHarvestSecret(string candidate) =>
        candidate.Length == ApiKeyConstants.Marker.Length + ApiKeyConstants.RandomLength
        && candidate.StartsWith(ApiKeyConstants.Marker, StringComparison.Ordinal)
        && candidate.AsSpan(ApiKeyConstants.Marker.Length).IndexOfAnyExcept(ApiKeyOptions.Alphabet) < 0;
}
