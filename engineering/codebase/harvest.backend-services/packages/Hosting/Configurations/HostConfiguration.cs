using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;
using WoW.Two.Sdk.Beta.Be.Meta;
using Harvest.Api.Console;
using Harvest.Api.Registration.Access;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Caching.Postgres;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Identity.Bff;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration;

namespace Harvest.Api.Configurations;

/// <summary>Extends the host builder and the web application for startup wiring.</summary>
public static partial class HostConfiguration
{
    /// <summary>Configures the application builder (services).</summary>
    /// <param name="builder">The web application builder to configure.</param>
    public static WebApplicationBuilder Configure(this WebApplicationBuilder builder)
    {
        // Lay the SDK boot floor before any domain seam. It reads the settings file a target mounts
        // (appsettings.Target.json) and, on a local run, this machine's (appsettings.Local.json). The hosted pages and
        // the console are served from the same host.
        builder.AddApiDefaults(options =>
        {
            options.ServiceName = "harvest";
            options.EnableOtlpExporters = false;
            options.EnableHttpsRedirection = builder.Configuration.GetValue<bool>(HarvestConsoleOptions.SectionName + ":IsIdentityEnabled")
                || !builder.Environment.IsDevelopment();
            options.SpaHosting = _ => { };
            options.TrustProxiesFrom(builder.Configuration.GetSection("Deployment"));
        });

        return builder
            .AddHarvestModule()
            .AddApiKeyAccess()
            .AddConsoleIdentity()
            .AddControllers();
    }

    /// <summary>Brings the store up, then configures middleware and endpoints.</summary>
    /// <param name="app">The built web application to configure.</param>
    public static async Task<WebApplication> ConfigureAsync(this WebApplication app)
    {
        if (app.Configuration.IsDeployed())
        {
            await app.Services.VerifyMigrationLevelsOnStartupAsync(app.Lifetime.ApplicationStopping);
            await app.Services.VerifyHarvestStoreAsync(app.Lifetime.ApplicationStopping);
        }
        else
            await app.Services.MigrateHarvestHostAsync("startup", app.Lifetime.ApplicationStopping);
        var hasIdentity = app.Services.GetRequiredService<IOptions<HarvestConsoleOptions>>().Value.IsIdentityEnabled;

        // SDK pipeline: secure headers, compression, the SPA bundle, the key gate on /api, /health (database readiness)
        // and the SPA fallback.
        app.UseApiDefaults(identity =>
        {
            if (hasIdentity)
                identity.UseIdentityBff();
            else
            {
                identity.UseAuthentication();
                identity.UseApiKeyAccessGate();
                identity.UseAuthorization();
            }
        });
        if (hasIdentity) app.MapIdentityBffEndpoints();
        app.MapControllers().RequireAuthorization(AccessPolicies.AnyKey);

        return app;
    }

    /// <summary>Applies Harvest's store and durable session cache on a local run. Deployed images use the SDK migrate command.</summary>
    /// <param name="services">The configured host services.</param>
    /// <param name="appliedBy">The migration audit actor.</param>
    /// <param name="cancellationToken">The token that cancels the migration run.</param>
    /// <returns>The migration operation.</returns>
    public static async Task MigrateHarvestHostAsync(this IServiceProvider services, string appliedBy = "startup", CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(services);
        await services.MigrateHarvestOnStartupAsync(appliedBy, cancellationToken);
        var cacheMigrations = services.GetRequiredService<IMigrationSetRegistry>().GetRunner(PostgresDistributedCacheConstants.MigrationSetName);
        (await cacheMigrations.ApplyPendingAsync(appliedBy, cancellationToken)).ValueOrThrow();
    }
}
