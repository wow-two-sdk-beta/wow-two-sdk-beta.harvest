using WoW.Two.Sdk.Beta.Be.Foundation.Options;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Marketplace;

namespace HarvestSources.Api.Configurations;

public static partial class HostConfiguration
{
    /// <summary>Hosts the approved public source catalog without private provider integrations.</summary>
    private static WebApplicationBuilder AddPublicCatalog(this WebApplicationBuilder builder)
    {
        builder.Services.AddControllers().ConfigureApplicationPartManager(manager =>
        {
            var hostAssembly = typeof(Program).Assembly.GetName().Name;
            foreach (var part in manager.ApplicationParts.Where(part => part.Name == hostAssembly).ToArray())
                manager.ApplicationParts.Remove(part);
        });
        return builder;
    }

    /// <summary>Registers the private event and listing sources over public Harvest.</summary>
    private static WebApplicationBuilder AddSources(this WebApplicationBuilder builder)
    {
        builder.Services.AddOcharoEventSources();
        builder.Services.AddHarvestListingSources();
        return builder;
    }

    /// <summary>Registers provider settings with the SDK's boot and deployment validation.</summary>
    private static WebApplicationBuilder AddProviderSettings(this WebApplicationBuilder builder)
    {
        builder.Services.AddHarvestIntegrations();
        builder.Services.AddValidatedSettings<HarvestIntegrationOptions>(builder.Configuration, "HarvestIntegrations", static _ => { });
        builder.Services.AddHarvestInstagram();
        builder.Services.AddValidatedSettings<InstagramOptions>(builder.Configuration, "HarvestInstagram", rules => rules
            .Validate(options => !options.Enabled || options.PageSize is >= 1 and <= 50
                && options.MaximumPages is >= 1 and <= 20 && options.MaximumResponseBytes is >= 1024 and <= 8 * 1024 * 1024
                && options.Timeout > TimeSpan.Zero && options.Timeout <= TimeSpan.FromMinutes(1),
                "Enabled Instagram collection requires valid page, byte and timeout limits."));
        return builder;
    }

    /// <summary>Registers the source-management endpoints in the shared HTTP host.</summary>
    private static WebApplicationBuilder AddSourceControllers(this WebApplicationBuilder builder)
    {
        builder.Services.AddControllers().AddApplicationPart(typeof(Program).Assembly);
        return builder;
    }

}
