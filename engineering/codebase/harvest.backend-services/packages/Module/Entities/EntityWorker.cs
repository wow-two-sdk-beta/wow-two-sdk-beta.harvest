using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;

/// <summary>Drains the resolution queue while the entity layer and the host's engine worker are enabled.</summary>
internal sealed class EntityWorker(
    IServiceScopeFactory scopes,
    IOptionsMonitor<EntityOptions> options,
    IOptions<HarvestEngineOptions> engine,
    TimeProvider clock,
    ILogger<EntityWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!engine.Value.Enabled || !options.CurrentValue.Enabled)
            return;
        while (!stoppingToken.IsCancellationRequested)
        {
            int claimed = 0;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                claimed = await scope.ServiceProvider.GetRequiredService<IEntityBroker>().ProcessDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Entity resolution tick failed; claimed posts retry after their lease");
            }
            if (claimed == 0)
                await Task.Delay(options.CurrentValue.PollInterval, clock, stoppingToken);
        }
    }
}
