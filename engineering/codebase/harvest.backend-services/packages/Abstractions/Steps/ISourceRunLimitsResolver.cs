using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Resolves saved collection bounds into the provider policy frozen for a new run.</summary>
public interface ISourceRunLimitsResolver
{
    /// <summary>Resolves provider pagination and saved collection bounds without changing the stored pipeline.</summary>
    /// <param name="source">The source selected for the run.</param>
    /// <param name="limits">The pipeline's saved bounds after work-kind defaults are applied.</param>
    /// <param name="kind">The work being queued.</param>
    /// <returns>The limits frozen for every work item in this run.</returns>
    PipelineLimits ResolveRunLimits(SourceModel source, PipelineLimits limits, WorkKind kind);
}
