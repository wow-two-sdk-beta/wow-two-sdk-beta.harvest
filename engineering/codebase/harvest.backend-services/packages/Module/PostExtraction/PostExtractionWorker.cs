using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

/// <summary>Drains the extraction queue while the host composes AI and demand and its engine worker is enabled.</summary>
internal sealed class PostExtractionWorker(
    IServiceScopeFactory scopes,
    IOptions<PostExtractionOptions> options,
    IOptions<HarvestEngineOptions> engine,
    TimeProvider clock,
    ILogger<PostExtractionWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!engine.Value.Enabled)
            return;
        await using (var probe = scopes.CreateAsyncScope())
            if (!probe.ServiceProvider.GetRequiredService<PostExtractionGate>().Enabled)
                return;
        while (!stoppingToken.IsCancellationRequested)
        {
            int claimed = 0;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                claimed = await scope.ServiceProvider.GetRequiredService<IPostExtractionBroker>().ProcessDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Post extraction tick failed; claimed posts retry after their lease");
            }
            if (claimed == 0)
                await Task.Delay(options.Value.PollInterval, clock, stoppingToken);
        }
    }
}
