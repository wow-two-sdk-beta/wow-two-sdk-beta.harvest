using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;

/// <summary>Runs explicitly enabled schedules and claims until the host stops.</summary>
internal sealed class HarvestWorker(
    IServiceScopeFactory scopes,
    IOptions<HarvestEngineOptions> options,
    TimeProvider clock,
    ILogger<HarvestWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!options.Value.Enabled)
            return;
        while (!stoppingToken.IsCancellationRequested)
        {
            bool worked = false;
            try
            {
                await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                HarvestEngine engine = scope.ServiceProvider.GetRequiredService<HarvestEngine>();
                await engine.QueueDueAsync(stoppingToken);
                worked = await engine.ProcessNextAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Harvest queue tick failed; expired work remains recoverable");
            }
            if (!worked)
                await Task.Delay(options.Value.PollInterval, clock, stoppingToken);
        }
    }
}
