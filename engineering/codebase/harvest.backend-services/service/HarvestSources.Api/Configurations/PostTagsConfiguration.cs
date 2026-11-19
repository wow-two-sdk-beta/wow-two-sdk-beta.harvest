using WoW.Two.Sdk.Beta.Be.Ai.Providers;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace HarvestSources.Api.Configurations;

internal static class PostTagsConfiguration
{
    /// <summary>Composes the post-tags pass, its eval subject source and its worker on a host whose shared AI is on
    /// (<see cref="RentalTermsConfiguration.EnabledSetting"/>); <c>Harvest:Processing:Tags:Enabled</c> then turns queueing
    /// on. Routes whose provider has no configured key leave the ready list.</summary>
    internal static void AddPostTagsAi(this WebApplicationBuilder builder)
    {
        if (!RentalTermsConfiguration.IsEnabled(builder.Configuration))
            return;

        builder.Services.AddHarvestPostTags()
            .BindConfiguration(PostTagsConstants.SectionName)
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
        builder.Services.AddHarvestPostTagsWorker();
    }
}
