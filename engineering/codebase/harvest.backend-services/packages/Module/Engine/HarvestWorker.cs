using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;

/// <summary>Runs explicitly enabled schedules and claims until the host stops.</summary>
/// <remarks>One dispatcher queues the due schedules and fills the worker's slots, and each claim runs as its own task in its own
/// service scope. The shared slot takes the claims of every run that fetches one item at a time, one claim at a time; each of
/// <see cref="HarvestEngineOptions.ParallelSlots"/> parallel slots takes a claim of a run whose harvest stage sets parallel
/// fetches. With no such run, only the shared slot works: one claim at a time. When nothing was claimed, the dispatcher waits for
/// <see cref="HarvestEngineOptions.PollInterval"/> or for a running claim to end, whichever comes first, because a run's own
/// completion locks its run row and the claim query skips locked rows, so a full wait would leave the slots idle.</remarks>
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
        Task? shared = null;
        var parallel = new List<Task>();
        try
        {
            while (!stoppingToken.IsCancellationRequested)
            {
                bool claimed = false;
                try
                {
                    await using AsyncServiceScope scope = scopes.CreateAsyncScope();
                    HarvestEngine engine = scope.ServiceProvider.GetRequiredService<HarvestEngine>();
                    await engine.QueueDueAsync(stoppingToken);
                    if (shared is null or { IsCompleted: true }
                        && await engine.ClaimAsync(HarvestEngine.ClaimPool.Shared, stoppingToken) is { } sharedClaim)
                    {
                        shared = RunAsync(sharedClaim, stoppingToken);
                        claimed = true;
                    }
                    parallel.RemoveAll(task => task.IsCompleted);
                    while (parallel.Count < options.Value.ParallelSlots
                        && await engine.ClaimAsync(HarvestEngine.ClaimPool.Parallel, stoppingToken) is { } parallelClaim)
                    {
                        parallel.Add(RunAsync(parallelClaim, stoppingToken));
                        claimed = true;
                    }
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception error)
                {
                    logger.LogError(error, "Harvest queue tick failed; expired work remains recoverable");
                }
                // A finished claim is dropped here, so the wait below never hands the finished task to WhenAny, which would
                // return at once and make an idle worker re-query the queue with no poll interval.
                if (shared is { IsCompleted: true })
                    shared = null;
                parallel.RemoveAll(task => task.IsCompleted);
                if (!claimed)
                    await WaitAsync(shared, parallel, stoppingToken);
            }
        }
        finally
        {
            // A stopped host waits for its claims to see the cancellation; a claim cut short stays recoverable by its lease.
            await Task.WhenAll([.. parallel, .. shared is null ? [] : new[] { shared }]);
        }
    }

    /// <summary>Waits for the poll interval or for a claim that is still running to end, whichever comes first; a claim that has
    /// already ended is not waited for.</summary>
    private async Task WaitAsync(Task? shared, List<Task> parallel, CancellationToken stoppingToken)
    {
        using var cancel = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken);
        Task poll = Task.Delay(options.Value.PollInterval, clock, cancel.Token);
        try
        {
            var running = new List<Task>(parallel.Count + 2) { poll };
            running.AddRange(parallel.Where(task => !task.IsCompleted));
            if (shared is { IsCompleted: false })
                running.Add(shared);
            await Task.WhenAny(running);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping; the loop ends at its next check.
        }
        finally
        {
            await cancel.CancelAsync();
            try { await poll; } catch (OperationCanceledException) { /* The poll was only a timer for this wait. */ }
        }
    }

    /// <summary>Runs one claim as its own task in its own scope; a failure is logged and the claim stays recoverable by its lease.</summary>
    private Task RunAsync(HarvestEngine.WorkClaim claim, CancellationToken stoppingToken) => Task.Run(async () =>
    {
        try
        {
            await using AsyncServiceScope scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<HarvestEngine>().ExecuteAsync(claim, stoppingToken);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // The host is stopping.
        }
        catch (Exception error)
        {
            logger.LogError(error, "Harvest queue tick failed; expired work remains recoverable");
        }
    }, CancellationToken.None);
}
