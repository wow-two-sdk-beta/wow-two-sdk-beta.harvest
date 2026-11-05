using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;
using WoW.Two.Sdk.Beta.Be.Meta;
using Harvest.Api.Registration.Access;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
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
        // (appsettings.Target.json) and, on a local run, this machine's (appsettings.Local.json).
        builder.AddApiDefaults(options =>
        {
            options.ServiceName = "harvest";
            options.EnableOtlpExporters = false;
            options.EnableHttpsRedirection = !builder.Environment.IsDevelopment();
            options.TrustProxiesFrom(builder.Configuration.GetSection("Deployment"));
        });

        return builder
            .AddHarvestModule()
            .AddHarvestAccess()
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
            await app.Services.MigrateHarvestOnStartupAsync("startup", app.Lifetime.ApplicationStopping);
        var serviceAccess = app.Services.GetRequiredService<IOptions<HarvestServiceAccessOptions>>().Value.Enabled;

        app.UseApiDefaults(access =>
        {
            access.UseAuthentication();
            if (!serviceAccess)
                access.UseApiKeyAccessGate();
            access.UseAuthorization();
        });
        app.MapControllers().RequireAuthorization(AccessPolicies.AnyCaller);

        return app;
    }
}
