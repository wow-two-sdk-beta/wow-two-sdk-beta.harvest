using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;

/// <summary>Resolves a Process stage's processor and the key its results are stored under.</summary>
internal static class CaptureProcessors
{
    internal static string ResolveKey(PipelineStageOptions? options) => options?.Processor ?? ICaptureProcessor.DefaultKey;

    internal static ICaptureProcessor? Resolve(IServiceProvider services, PipelineStageOptions? options) =>
        services.GetKeyedService<ICaptureProcessor>(ResolveKey(options));

    /// <summary>Names a stored result by its processor, and by the model route when a model processor ran.</summary>
    internal static string ResultKey(PipelineStageOptions? options)
    {
        string key = ResolveKey(options);
        if (key == ICaptureProcessor.DefaultKey)
            return key;
        if (!string.IsNullOrWhiteSpace(options?.Model))
            key += $":{options.Model}";
        return options?.PromptVersion is { } version ? $"{key}:prompt-{version}" : key;
    }

    /// <summary>Checks a processor against the source's current mode and its stage settings.</summary>
    /// <param name="processor">The stage's processor; none when unregistered.</param>
    /// <param name="source">The source as it stands now.</param>
    /// <param name="options">The stage settings.</param>
    /// <param name="automatic">Whether nobody started the work by hand: a schedule queued it, or a schedule is being
    /// enabled. A model processor then needs <see cref="ModelProcessingMode.Auto"/>; a run started by hand needs only a
    /// mode other than <see cref="ModelProcessingMode.Off"/>.</param>
    internal static AppError? Preflight(ICaptureProcessor? processor, SourceModel source, PipelineStageOptions options, bool automatic = false) =>
        SourceError(processor, source, automatic) ?? processor!.ValidateConfiguration(options);

    private static AppError? SourceError(ICaptureProcessor? processor, SourceModel source, bool automatic) =>
        processor is null ? AppErrorFactory.Conflict("The capture processor is unavailable.")
        : !processor.Serves(source) ? AppErrorFactory.Validation("This processor does not support the selected source.")
        : processor.RequiresAi && source.ModelProcessing == ModelProcessingMode.Off
            ? AppErrorFactory.Forbidden("Model processing is disabled for this source.")
        : processor.RequiresAi && automatic && source.ModelProcessing != ModelProcessingMode.Auto
            ? AppErrorFactory.Forbidden("Model processing is manual for this source: start the Process stage by hand.")
        : null;

    internal static async Task<AppError?> PreflightAsync(ICaptureProcessor? processor, SourceModel source,
        PipelineStageOptions options, CancellationToken ct, bool automatic = false) =>
        SourceError(processor, source, automatic) ?? await processor!.ValidateConfigurationAsync(options, ct);

    /// <summary>Lists the registered processors by key, each once, with the name the console shows.</summary>
    internal static IReadOnlyDictionary<string, string> PublicLabels(IServiceProvider services, SourceModel source) =>
        new SortedDictionary<string, string>(services.GetKeyedServices<ICaptureProcessor>(KeyedService.AnyKey)
            .Where(processor => processor.Serves(source))
            .DistinctBy(processor => processor.Key, StringComparer.Ordinal)
            .ToDictionary(processor => processor.Key, processor => processor.Label, StringComparer.Ordinal), StringComparer.Ordinal);

    internal static bool IsRegistered(IServiceProviderIsKeyedService services, string key) =>
        services.IsKeyedService(typeof(ICaptureProcessor), key);
}
