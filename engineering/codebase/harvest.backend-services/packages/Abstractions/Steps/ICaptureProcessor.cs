using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Interprets a saved source reading without recapturing its page.</summary>
public interface ICaptureProcessor
{
    /// <summary>The built-in model-free summary.</summary>
    public const string DefaultKey = "saved-reading@1";

    /// <summary>Gets the stable key under which the host registers this processor.</summary>
    string Key { get; }

    /// <summary>Gets the name the console shows for this processor; the key when the processor names none.</summary>
    string Label => Key;

    /// <summary>Whether this processor may send saved text to a model.</summary>
    bool RequiresAi => false;

    /// <summary>Whether the processor supports this source.</summary>
    bool Serves(SourceModel source) => true;

    /// <summary>Checks readiness without making a model call or changing settings.</summary>
    AppError? ValidateConfiguration(PipelineStageOptions options) => null;

    /// <summary>Checks database-backed readiness without calling a model.</summary>
    Task<AppError?> ValidateConfigurationAsync(PipelineStageOptions options, CancellationToken cancellationToken = default) =>
        Task.FromResult(ValidateConfiguration(options));

    /// <summary>Gets the processor's operations in execution order, or an empty list for one operation.</summary>
    IReadOnlyList<string> OperationKeys => [];

    /// <summary>Processes an existing reading and returns the result to store.</summary>
    Task<CaptureProcessResult> ProcessAsync(CaptureProcessInput input, CancellationToken cancellationToken = default);
}
