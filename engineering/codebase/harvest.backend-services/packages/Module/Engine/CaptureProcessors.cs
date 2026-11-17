using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
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
        return key == ICaptureProcessor.DefaultKey || string.IsNullOrWhiteSpace(options?.Model) ? key : $"{key}:{options.Model}";
    }

    internal static string[] PublicKeys(IServiceProvider services) => services.GetKeyedServices<ICaptureProcessor>(KeyedService.AnyKey)
        .Select(processor => processor.Key).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    internal static bool IsRegistered(IServiceProviderIsKeyedService services, string key) =>
        services.IsKeyedService(typeof(ICaptureProcessor), key);
}
