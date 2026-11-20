using WoW.Two.Sdk.Beta.Be.Harvest.Module.Budget;

namespace HarvestSources.Api.Configurations;

internal static class ProcessingBudgetConfiguration
{
    /// <summary>Composes the AI spend guard and binds <c>Harvest:Processing:Budget</c>, validated at start so a bad value fails
    /// <c>config check</c>. The guard needs no AI flag: without a <c>LimitUsd</c>, or without shared AI, its state is
    /// <c>off</c> and nothing changes.</summary>
    internal static void AddProcessingBudget(this WebApplicationBuilder builder) =>
        builder.Services.AddHarvestProcessingBudget().BindConfiguration(ProcessingBudgetConstants.SectionName).ValidateOnStart();
}
