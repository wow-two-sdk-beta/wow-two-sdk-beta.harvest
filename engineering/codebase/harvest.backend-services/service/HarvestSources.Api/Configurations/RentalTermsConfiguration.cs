using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Ai.Module;
using WoW.Two.Sdk.Beta.Be.Ai.Providers;
using WoW.Two.Sdk.Beta.Be.Data.Migrations.Bespoke;
using WoW.Two.Sdk.Beta.Be.Foundation.Configuration;
using WoW.Two.Sdk.Beta.Be.Foundation.Options;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Marketplace;

namespace HarvestSources.Api.Configurations;

internal static class RentalTermsConfiguration
{
    internal const string EnabledSetting = "Harvest:Processing:Ai:Enabled";

    internal static void AddRentalTermsAi(this WebApplicationBuilder builder)
    {
        if (!IsEnabled(builder.Configuration))
            return;

        builder.Services.AddAiProviders(builder.Configuration);
        builder.Services.AddAi().BindConfiguration("Ai:Module")
            .Configure<IOptions<HarvestModuleOptions>>((options, harvest) =>
                options.ConnectionString = harvest.Value.ConnectionString);
        builder.Services.AddValidatedSettings<RentalTermsOptions>(builder.Configuration, RentalTermsOptions.SectionName,
            rules => rules.PostConfigure<IAiModelCaller>((options, caller) =>
            {
                options.Models = [.. options.Models
                    .Select(ParseRoute)
                    .OfType<AiModelRef>()
                    .Where(model => caller.IsConfigured(model.Provider))
                    .Select(model => model.ToString())
                    .Distinct(StringComparer.Ordinal)];
            }));
    }

    internal static async Task MigrateRentalTermsAiLocallyAsync(this WebApplication app, CancellationToken ct)
    {
        if (!IsEnabled(app.Configuration) || app.Configuration.IsDeployed())
            return;

        var runner = app.Services.GetRequiredService<IMigrationSetRegistry>().GetRunner(AiModuleConstants.MigrationSetName);
        (await runner.ApplyPendingAsync("startup", ct)).ValueOrThrow();
    }

    internal static bool IsEnabled(IConfiguration configuration) =>
        (configuration["HarvestSources:Audience"] is null or "internal") && configuration.GetValue<bool>(EnabledSetting);

    internal static AiModelRef? ParseRoute(string route) =>
        AiModelRef.TryParse(route, out var model) && model is not null && !model.Provider.Any(char.IsWhiteSpace)
            && !model.Model.Any(char.IsWhiteSpace) ? model : null;
}
