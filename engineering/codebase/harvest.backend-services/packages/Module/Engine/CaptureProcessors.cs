using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;

/// <summary>Resolves configured and pre-existing processor selections.</summary>
internal static class CaptureProcessors
{
    internal static string ResolveKey(PipelineStageOptions? options) =>
        options?.Processor ?? (string.IsNullOrWhiteSpace(options?.Model)
            ? ICaptureProcessor.DefaultKey : ICaptureProcessor.LegacyModelKey);

    internal static ICaptureProcessor? Resolve(IServiceProvider services, PipelineStageOptions? options) =>
        services.GetKeyedService<ICaptureProcessor>(ResolveKey(options));

    internal static string ResultKey(PipelineStageOptions? options)
    {
        string key = ResolveKey(options);
        if (key == ICaptureProcessor.LegacyModelKey)
            key = ICaptureProcessor.DefaultKey;
        return string.IsNullOrWhiteSpace(options?.Model) ? key : $"{key}:{options.Model}";
    }

    internal static string[] PublicKeys(IServiceProvider services) => services.GetKeyedServices<ICaptureProcessor>(KeyedService.AnyKey)
        .Select(processor => processor.Key).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToArray();

    internal static bool IsRegistered(IServiceProviderIsKeyedService services, string key) =>
        services.IsKeyedService(typeof(ICaptureProcessor), key);
}
