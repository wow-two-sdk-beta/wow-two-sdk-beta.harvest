using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

/// <summary>Drains the post-tags queue while tagging is on and the host's engine worker is enabled.</summary>
internal sealed class PostTagsWorker(
    IServiceScopeFactory scopes,
    PostTagsGate gate,
    IOptions<PostTagsOptions> options,
    IOptions<HarvestEngineOptions> engine,
    TimeProvider clock,
    ILogger<PostTagsWorker> logger) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (!gate.Enabled || !engine.Value.Enabled)
            return;
        while (!stoppingToken.IsCancellationRequested)
        {
            int claimed = 0;
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                claimed = await scope.ServiceProvider.GetRequiredService<IPostTagsBroker>().ProcessDueAsync(stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Post tagging tick failed; claimed posts retry after their lease");
            }
            if (claimed == 0)
                await Task.Delay(options.Value.PollInterval, clock, stoppingToken);
        }
    }
}
