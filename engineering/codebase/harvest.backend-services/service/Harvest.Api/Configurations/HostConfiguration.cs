using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;
using WoW.Two.Sdk.Beta.Be.Meta;

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
            options.EnableHttpsRedirection = !builder.Environment.IsDevelopment();
            options.SpaHosting = _ => { };
            options.TrustProxiesFrom(builder.Configuration.GetSection("Deployment"));
        });

        return builder
            .AddHarvestModule()
            .AddApiKeyAccess()
            .AddControllers();
    }

    /// <summary>Brings the store up, then configures middleware and endpoints.</summary>
    /// <param name="app">The built web application to configure.</param>
    public static async Task<WebApplication> ConfigureAsync(this WebApplication app)
    {
        // Create the database if missing, refuse one that is not UTF-8 or lacks pg_trgm, apply the module's migration
        // set, and refuse one whose trigrams skip Cyrillic — all before serving.
        await app.Services.MigrateHarvestOnStartupAsync("startup", app.Lifetime.ApplicationStopping);

        // SDK pipeline: secure headers, compression, the SPA bundle, the key gate on /api, /health (database readiness)
        // and the SPA fallback.
        app.UseApiDefaults(identity =>
        {
            identity.UseAuthentication();
            identity.UseApiKeyAccessGate();
            identity.UseAuthorization();
        });
        app.MapControllers();

        return app;
    }
}
