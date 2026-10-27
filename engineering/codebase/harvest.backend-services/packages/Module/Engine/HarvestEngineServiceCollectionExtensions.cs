using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;

/// <summary>Registers the engine separately from opting into background work.</summary>
public static class HarvestEngineServiceCollectionExtensions
{
    /// <summary>Registers durable execution services; this starts no background work.</summary>
    public static OptionsBuilder<HarvestEngineOptions> AddHarvestEngine(this IServiceCollection services)
    {
        services.TryAddScoped<IRunBroker, ModuleRunBroker>();
        services.TryAddScoped<HarvestEngine>();
        return services.AddOptions<HarvestEngineOptions>()
            .Validate(options => options.LeaseDuration >= TimeSpan.FromSeconds(10), "A lease must last at least ten seconds.")
            .Validate(options => options.PollInterval > TimeSpan.Zero && options.RefusalBackoff > TimeSpan.Zero,
                "Polling and refusal backoff must be positive.")
            .Validate(options => options.MaxItems > 0 && options.MaxRequests > 0 && options.MaxBytes > 0
                && options.MaxDuration > TimeSpan.Zero, "Run limits must be positive.");
    }

    /// <summary>Opts into scheduled pipeline work in this host.</summary>
    public static IServiceCollection AddHarvestWorker(this IServiceCollection services)
    {
        services.AddHarvestEngine();
        services.AddHostedService<HarvestWorker>();
        return services;
    }

    /// <summary>Registers a source adapter explicitly in code; later adapters take precedence over generic readers.</summary>
    public static IServiceCollection AddHarvestBroker<T>(this IServiceCollection services) where T : class, ISourceAdapter
    {
        services.AddSingleton<ISourceAdapter, T>();
        return services;
    }
}
