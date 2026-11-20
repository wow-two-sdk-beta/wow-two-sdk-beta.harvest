using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Ai.Inferences;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Budget;

/// <summary>Sums the AI module's inference log over the current window and keeps the result for the refresh interval, so
/// the workers' polling reads memory, not the database.</summary>
/// <remarks>
///   - the log is read in a scope of its own (<see cref="IInferencesBroker"/> is scoped) and never throws into a worker
///   - a failed read keeps the last good reading; with none, a host with a limit fails closed at <see cref="BudgetState.Hard"/>
///   - one refresh runs at a time: callers that arrive meanwhile wait for it and take its reading
/// </remarks>
internal sealed class ProcessingBudget(
    IOptions<ProcessingBudgetOptions> options,
    IServiceScopeFactory scopes,
    TimeProvider time,
    ILogger<ProcessingBudget> logger) : IProcessingBudget
{
    private readonly SemaphoreSlim refresh = new(1, 1);
    private volatile Held? held;
    private BudgetReading? lastGood;
    private BudgetState? logged;

    /// <inheritdoc />
    public async Task<BudgetReading> ReadAsync(CancellationToken cancellationToken = default)
    {
        if (held is { } hit && time.GetUtcNow() < hit.ExpiresAtUtc)
            return hit.Reading;
        await refresh.WaitAsync(cancellationToken);
        try
        {
            var now = time.GetUtcNow();
            if (held is { } fresh && now < fresh.ExpiresAtUtc)
                return fresh.Reading;
            var reading = await ComputeAsync(now, cancellationToken);
            held = new Held(reading, now + options.Value.RefreshInterval);
            Log(reading);
            return reading;
        }
        finally
        {
            refresh.Release();
        }
    }

    private async Task<BudgetReading> ComputeAsync(DateTimeOffset now, CancellationToken ct)
    {
        var settings = options.Value;
        await using var scope = scopes.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<IInferencesBroker>() is not { } inferences)
            return BudgetCalculator.Compose(settings, now, []) with { State = BudgetState.Off, ProjectedMicroUsd = null };

        var to = now.AddSeconds(1);
        var start = BudgetCalculator.Window(settings, now).Start;
        IReadOnlyList<BudgetUsageRow> rows = [];
        // A window that starts later (a future SinceUtc) has no spend and no period the log would accept.
        if (start < to)
        {
            try
            {
                if (await inferences.TotalsAsync(start, to, ct)
                    is not Result<IReadOnlyList<InferenceTotals>>.Success { Value: var totals })
                    return Unreadable(settings, now, "the inference log refused the read");
                rows = BudgetCalculator.Rows(totals);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogWarning(error, "The AI spend guard could not read the inference log");
                return Unreadable(settings, now, error.GetType().Name);
            }
        }

        var reading = BudgetCalculator.Compose(settings, now, rows);
        lastGood = reading;
        return reading;
    }

    private BudgetReading Unreadable(ProcessingBudgetOptions settings, DateTimeOffset now, string why)
    {
        if (settings.LimitUsd is null)
        {
            logger.LogDebug("The AI spend guard is off and could not read the inference log: {Why}", why);
            return BudgetCalculator.Compose(settings, now, []);
        }
        if (lastGood is { } good)
        {
            logger.LogWarning("The AI spend guard could not read the inference log ({Why}); it keeps the reading of {ReadAt:u}", why, good.ReadAtUtc);
            return good;
        }
        logger.LogWarning("The AI spend guard could not read the inference log ({Why}) and has no earlier reading; it holds model calls back", why);
        return BudgetCalculator.Compose(settings, now, []) with { State = BudgetState.Hard, ReadFailed = true };
    }

    /// <summary>Logs each change of state, once: a worker polls every few seconds and must not repeat it.</summary>
    private void Log(BudgetReading reading)
    {
        var before = logged;
        logged = reading.State;
        if (reading.ReadFailed || before == reading.State || before is null && reading.State is BudgetState.Off or BudgetState.Open)
            return;
        var level = reading.State is BudgetState.Soft or BudgetState.Hard ? LogLevel.Warning : LogLevel.Information;
        logger.Log(level, "The AI spend guard is {State}: {SpentUsd} of {LimitUsd} USD spent since {Start:u}",
            reading.State, reading.SpentUsd, reading.LimitUsd, reading.WindowStartUtc);
    }

    private sealed record Held(BudgetReading Reading, DateTimeOffset ExpiresAtUtc);
}
