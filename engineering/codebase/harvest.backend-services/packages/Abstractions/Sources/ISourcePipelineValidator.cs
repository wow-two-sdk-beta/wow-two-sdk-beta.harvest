using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Validates an adapter's saved pipeline configuration before the registry writes it, without provider I/O.</summary>
public interface ISourcePipelineValidator
{
    /// <summary>Checks a pipeline's scope and effective limits against the selected source adapter.</summary>
    /// <param name="source">The registered source owning the pipeline.</param>
    /// <param name="scope">The complete scope to save.</param>
    /// <param name="limits">The complete effective limits, including saved values preserved by an update.</param>
    /// <returns>Success when the configuration is supported; otherwise the validation error.</returns>
    Result ValidatePipeline(SourceModel source, PipelineScope scope, PipelineLimits limits);
}
