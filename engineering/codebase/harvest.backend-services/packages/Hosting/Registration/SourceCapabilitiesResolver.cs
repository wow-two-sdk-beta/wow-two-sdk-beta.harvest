using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace Harvest.Api.Registration;

/// <summary>Resolves the same adapter precedence as run planning without making a source request.</summary>
/// <param name="adapters">The runtime's explicitly installed adapters, in registration order.</param>
public sealed class SourceCapabilitiesResolver(IEnumerable<ISourceAdapter> adapters)
{
    /// <summary>Returns safe local capabilities for an already-authorized source.</summary>
    public SourceCapabilitiesModel Describe(SourceModel source)
    {
        ISourceAdapter? adapter = adapters.LastOrDefault(candidate => candidate.Serves(source));
        SourceConfigurationModel configuration = adapter is ISourceConfigurationProvider provider
            ? provider.DescribeConfiguration(source)
            : new()
            {
                DisplayName = adapter?.Key ?? "No installed adapter",
                Readiness = adapter is null ? SourceReadiness.Unavailable : SourceReadiness.Ready,
                Message = adapter is null ? "This runtime has no adapter for this source family." : "Remote access is checked during execution.",
                ScopeSchema = SourceScopeSchemas.WebUrl("Use a URL on the registered source host, including its filters.", false),
            };
        return new()
        {
            SourceId = source.Id,
            AdapterKey = adapter?.Key,
            WorkKinds = adapter?.Describe(source).Plans.Where(plan => plan.Value.Count > 0).Select(plan => plan.Key).Order().ToArray() ?? [],
            Configuration = configuration,
        };
    }
}
