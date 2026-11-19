using WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;

namespace HarvestSources.Api.Configurations;

internal static class EntitiesConfiguration
{
    /// <summary>Composes the entity layer and its worker; <c>Harvest:Entities:Enabled</c> then lets a done extraction queue
    /// its post's resolution. The layer needs no AI: it reads stored extraction answers.</summary>
    internal static void AddEntities(this WebApplicationBuilder builder)
    {
        builder.Services.AddHarvestEntities().BindConfiguration(EntityConstants.SectionName).ValidateOnStart();
        builder.Services.AddHarvestEntitiesWorker();
    }
}
