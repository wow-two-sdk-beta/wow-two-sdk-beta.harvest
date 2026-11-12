namespace HarvestSources.Api.Configurations;

/// <summary>Extends the host builder and the web application for startup wiring.</summary>
public static partial class HostConfiguration
{
    /// <summary>Configures the application builder (services).</summary>
    /// <param name="builder">The application builder.</param>
    /// <returns>The configured builder.</returns>
    public static WebApplicationBuilder Configure(this WebApplicationBuilder builder)
    {
        global::Harvest.Api.Configurations.HostConfiguration.Configure(builder);
        return builder.Configuration["HarvestSources:Audience"] switch
        {
            null or "internal" => builder.AddSources().AddProviderSettings().AddSourceControllers(),
            "public" => builder.AddPublicCatalog(),
            _ => throw new InvalidOperationException("HarvestSources:Audience must be internal or public."),
        };
    }

    /// <summary>Runs startup tasks, then configures middleware and endpoints.</summary>
    /// <param name="app">The application.</param>
    /// <returns>The configured application.</returns>
    public static Task<WebApplication> ConfigureAsync(this WebApplication app) =>
        global::Harvest.Api.Configurations.HostConfiguration.ConfigureAsync(app);
}
