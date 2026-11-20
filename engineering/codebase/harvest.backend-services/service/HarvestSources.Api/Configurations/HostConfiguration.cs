using WoW.Two.Sdk.Beta.Be.Harvest.Module.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Reprocessing;
using WoW.Two.Sdk.Beta.Be.Meta;

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
        global::Harvest.Api.Configurations.HostConfiguration.Configure(builder, ServeConsole);
        builder.AddRentalTermsAi();
        builder.AddProcessingBudget();
        builder.AddPostTagsAi();
        builder.AddPostExtractionAi();
        builder.AddSubscriptionSelectors();
        builder.AddEntities();
        builder.AddAiApiForOperators();
        builder.Services.AddHarvestPostReprocessing();
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
        app.MapAiApiForOperators();
        return app;
    }

    /// <summary>Serves the console the image builds into <c>wwwroot</c>: its files before routing and authentication,
    /// <c>index.html</c> for every other unmatched route, so a deep link reloads, and a JSON 404 for an unmatched
    /// <c>/api</c> route, never the page. Without a built console a page route answers 404. The console shows a post's
    /// images from the source's own host while SCFD's copy is pending, so no embedder policy blocks them.</summary>
    /// <param name="options">The SDK API defaults Harvest's hosting configures.</param>
    private static void ServeConsole(ApiDefaultsOptions options)
    {
        options.SpaHosting = static _ => { };
        options.EnableCrossOriginEmbedderPolicy = false;
    }
}
