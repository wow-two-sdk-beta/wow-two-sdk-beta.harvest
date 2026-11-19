using WoW.Two.Sdk.Beta.Be.Harvest.Module.Registration;

namespace HarvestSources.Api.Configurations;

/// <summary>Extends the host builder and the web application for startup wiring.</summary>
public static partial class HostConfiguration
{
    /// <summary>Configures the application builder (services).</summary>
    /// <param name="builder">The application builder.</param>
    /// <returns>The configured builder.</returns>
    public static WebApplicationBuilder Configure(this WebApplicationBuilder builder)
    {
        builder.AddProcessingModels();
        global::Harvest.Api.Configurations.HostConfiguration.Configure(builder);
        builder.AddRentalTermsAi();
        builder.AddPostTagsAi();
        builder.AddPostExtractionAi();
        return builder.Configuration["HarvestSources:Audience"] switch
        {
            null or "internal" => builder.AddSources().AddProviderSettings().AddSourceAssets().AddSourceControllers(),
            "public" => builder.AddPublicCatalog(),
            _ => throw new InvalidOperationException("HarvestSources:Audience must be internal or public."),
        };
    }

    /// <summary>Runs startup tasks, then configures middleware and endpoints.</summary>
    /// <param name="app">The application.</param>
    /// <returns>The configured application.</returns>
    public static async Task<WebApplication> ConfigureAsync(this WebApplication app)
    {
        await global::Harvest.Api.Configurations.HostConfiguration.ConfigureAsync(app);
        await app.MigrateRentalTermsAiLocallyAsync(app.Lifetime.ApplicationStopping);
        await app.Services.ReconcileHarvestSourceSectionsAsync(app.Lifetime.ApplicationStopping);
        await app.SeedPipelineStagesAsync(app.Lifetime.ApplicationStopping);
        return app;
    }
}
