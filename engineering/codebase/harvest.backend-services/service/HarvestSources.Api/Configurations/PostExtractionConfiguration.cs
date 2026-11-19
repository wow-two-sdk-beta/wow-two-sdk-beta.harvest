using WoW.Two.Sdk.Beta.Be.Ai.Providers;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

namespace HarvestSources.Api.Configurations;

internal static class PostExtractionConfiguration
{
    /// <summary>Composes Layer 2 extraction, its configured demand gate and its worker on a host whose shared AI is on
    /// (<see cref="RentalTermsConfiguration.EnabledSetting"/>); <c>Harvest:Processing:Extraction:Wanted</c> then names the
    /// (entity type, domain) pairs that queue. Routes whose provider has no configured key leave the ready list.</summary>
    internal static void AddPostExtractionAi(this WebApplicationBuilder builder)
    {
        if (!RentalTermsConfiguration.IsEnabled(builder.Configuration))
            return;

        builder.Services.AddHarvestPostExtraction()
            .BindConfiguration(PostExtractionConstants.SectionName)
            .PostConfigure<IAiModelCaller>((options, caller) =>
            {
                options.Models = [.. options.Models
                    .Select(RentalTermsConfiguration.ParseRoute)
                    .OfType<AiModelRef>()
                    .Where(model => caller.IsConfigured(model.Provider))
                    .Select(model => model.ToString())
                    .Distinct(StringComparer.Ordinal)];
            })
            .ValidateOnStart();
        builder.Services.AddHarvestPostExtractionWorker();
    }
}
