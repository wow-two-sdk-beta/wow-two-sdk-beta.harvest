using HarvestSources.Api.Configurations;
using HarvestSources.Api.Entities;
using HarvestSources.Api.Extraction;
using HarvestSources.Api.Marketplace;
using HarvestSources.Api.Ocharo;
using HarvestSources.Api.Processing;
using HarvestSources.Api.Sources;
using HarvestSources.Api.Tagging;
using WoW.Two.Sdk.Beta.Be.Meta;

return await ServiceHost.RunAsync(args, builder => builder.Configure(), app => app.ConfigureAsync(),
    verbs => verbs
        .AddHostVerb(OcharoSetupVerb.Name, (services, verb, ct) =>
            OcharoSetupVerb.RunAsync(services, verb.Arguments, verb.Output, verb.Error, ct))
        .AddHostVerb(MarketplaceSetupVerb.Name, (services, verb, ct) =>
            MarketplaceSetupVerb.RunAsync(services, verb.Arguments, verb.Output, verb.Error, ct))
        .AddHostVerb(RentalTermsPromptVerb.Name, (services, verb, ct) =>
            RentalTermsPromptVerb.RunAsync(services, verb.Arguments, Console.OpenStandardInput(), verb.Output, verb.Error, ct))
        .AddHostVerb(PostTagsVerb.Name, (services, verb, ct) =>
            PostTagsVerb.RunAsync(services, verb.Arguments, Console.OpenStandardInput(), verb.Output, verb.Error, ct))
        .AddHostVerb(PostExtractionVerb.Name, (services, verb, ct) =>
            PostExtractionVerb.RunAsync(services, verb.Arguments, Console.OpenStandardInput(), verb.Output, verb.Error, ct))
        .AddHostVerb(EntitiesVerb.Name, (services, verb, ct) =>
            EntitiesVerb.RunAsync(services, verb.Arguments, verb.Output, verb.Error, ct))
        .AddHostVerb(ProcessingVerb.Name, (services, verb, ct) =>
            ProcessingVerb.RunAsync(services, verb.Arguments, verb.Output, verb.Error, ct))
        .AddHostVerb(SourcesVerb.Name, (services, verb, ct) =>
            SourcesVerb.RunAsync(services, verb.Arguments, verb.Output, verb.Error, ct)));

public partial class Program;
