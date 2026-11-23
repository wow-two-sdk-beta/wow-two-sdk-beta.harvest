using HarvestSources.Api.Entities;
using WoW.Two.Sdk.Beta.Be.Ai.Prompts;
using WoW.Two.Sdk.Beta.Be.Ai.Providers;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;

namespace HarvestSources.Api.Configurations;

internal static class EntitiesConfiguration
{
    /// <summary>Composes the entity layer and its worker; <c>Harvest:Entities:Enabled</c> then lets a done extraction queue
    /// its post's resolution. A configured matching model can advise on ambiguous event identities.</summary>
    internal static void AddEntities(this WebApplicationBuilder builder)
    {
        builder.Services.AddHarvestEntities().BindConfiguration(EntityConstants.SectionName).ValidateOnStart();
        builder.Services.AddHarvestEntitiesWorker();
        if (RentalTermsConfiguration.IsEnabled(builder.Configuration)
            && builder.Configuration["Harvest:Entities:MatchModel"] is { } route
            && RentalTermsConfiguration.ParseRoute(route) is { } model)
            builder.Services.AddScoped<IEventMatchAdvisor>(services => new HaikuEventMatchAdvisor(
                services.GetRequiredService<IAiChatClients>(), services.GetRequiredService<IPromptsBroker>(), model,
                services.GetRequiredService<ILogger<HaikuEventMatchAdvisor>>()));
    }
}
