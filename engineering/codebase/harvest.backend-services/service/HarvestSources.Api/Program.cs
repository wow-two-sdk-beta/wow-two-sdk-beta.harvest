using HarvestSources.Api.Configurations;
using HarvestSources.Api.Ocharo;
using WoW.Two.Sdk.Beta.Be.Meta;

return await ServiceHost.RunAsync(args, builder => builder.Configure(), app => app.ConfigureAsync(),
    verbs => verbs.AddHostVerb(OcharoSetupVerb.Name, (services, verb, ct) =>
        OcharoSetupVerb.RunAsync(services, verb.Arguments, verb.Output, verb.Error, ct)));

public partial class Program;
