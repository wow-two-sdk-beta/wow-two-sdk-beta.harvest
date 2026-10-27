using System.Net.Http.Headers;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Http.Safety;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;

/// <summary>Registers Harvest's direct fetching capability explicitly in a host.</summary>
public static class HarvestFetchingServiceCollectionExtensions
{
    /// <summary>Registers proxy-free clients, durable host pacing and the generic refusal detector.</summary>
    /// <param name="services">The host's service collection, already configured with <c>AddHarvest</c>.</param>
    /// <param name="configure">Optional transparent user-agent and resource limits.</param>
    /// <returns>The options builder for configuration binding.</returns>
    public static OptionsBuilder<HarvestFetchOptions> AddHarvestFetching(this IServiceCollection services, Action<HarvestFetchOptions>? configure = null)
    {
        ArgumentNullException.ThrowIfNull(services);
        services.TryAddSingleton(TimeProvider.System);
        var builder = services.AddOptions<HarvestFetchOptions>();
        if (configure is not null)
            builder.Configure(configure);
        builder.Validate(options => IsUserAgent(options.UserAgent), "Harvest fetch UserAgent must identify Harvest and be a valid HTTP user agent.")
            .Validate(options => options.MinimumDelay >= TimeSpan.FromSeconds(2) && options.MinimumDelay <= TimeSpan.FromSeconds(30), "Harvest fetch MinimumDelay must be between two and thirty seconds.")
            .Validate(options => options.Timeout > TimeSpan.Zero && options.Timeout <= TimeSpan.FromMinutes(10), "Harvest fetch Timeout must be positive and at most ten minutes.")
            .Validate(options => options.MaximumResponseBytes is >= 1024 and <= 100 * 1024 * 1024, "Harvest fetch MaximumResponseBytes must be between 1 KiB and 100 MiB.")
            .Validate(options => options.MaximumRedirects is >= 0 and <= 10, "Harvest fetch MaximumRedirects must be between zero and ten.")
            .Validate(options => options.RobotsCacheDuration > TimeSpan.Zero && options.RefusalBackoff >= TimeSpan.FromSeconds(1), "Harvest fetch cache and refusal intervals must be positive.");

        services.AddHttpClient(HarvestFetchClientFactory.TransportName, client => client.Timeout = System.Threading.Timeout.InfiniteTimeSpan)
            .AddSafeOutboundHttp(policy => policy.RequireHttps = false);
        services.TryAddSingleton<IHostFetchStore, PostgresHostFetchStore>();
        services.TryAddSingleton<IChallengeDetector, GenericChallengeDetector>();
        services.TryAddSingleton<IFetchClientFactory, HarvestFetchClientFactory>();
        services.TryAddSingleton<IPageFetcher, PageFetcher>();
        return builder;
    }

    private static bool IsUserAgent(string value)
    {
        if (string.IsNullOrWhiteSpace(value) || !value.Contains("Harvest", StringComparison.OrdinalIgnoreCase))
            return false;
        try
        {
            using var message = new HttpRequestMessage();
            var headers = message.Headers;
            headers.UserAgent.ParseAdd(value);
            return headers.UserAgent.FirstOrDefault()?.Product?.Name.Contains("Harvest", StringComparison.OrdinalIgnoreCase) == true;
        }
        catch (FormatException)
        {
            return false;
        }
    }
}
