using System.Text.Json;
using Microsoft.Extensions.Logging;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;

/// <summary>Records the operations of a single claim as it works.</summary>
internal sealed class StepTrace(
    NpgsqlDataSource database, StepItem item, string leaseOwner, TimeProvider clock, ILogger logger, CancellationToken workerToken)
    : ICaptureProcessTrace
{
    private readonly List<RunOperationModel> operations = [];

    internal string Snapshot() => StoreJson.Write(operations);

    internal async Task<int> BeginAsync(string key)
    {
        int index = operations.Count;
        operations.Add(new(key, clock.GetUtcNow()));
        await PublishAsync();
        return index;
    }

    internal void End(int index, StepOperationResult result)
        => operations[index] = operations[index] with { FinishedAtUtc = clock.GetUtcNow(), Result = result };

    Task<T> ICaptureProcessTrace.RunOperationAsync<T>(string key, Func<Task<T>> execute,
        Func<T, StepOperationResult>? describe) => RunAsync(key, execute, describe);

    internal async Task<T> RunAsync<T>(string key, Func<Task<T>> execute, Func<T, StepOperationResult>? describe = null)
    {
        int index = await BeginAsync(key);
        T value;
        try
        {
            value = await execute();
            End(index, describe?.Invoke(value) ?? new());
        }
        catch (Exception error)
        {
            End(index, Failure(error));
            await PublishAsync();
            throw;
        }
        await PublishAsync();
        return value;
    }

    internal async Task PublishAsync()
    {
        try
        {
            await using var connection = await database.OpenConnectionAsync(workerToken);
            await EngineSql.ExecuteAsync(connection, null, """
                UPDATE harvest_step_attempts a SET operations=@operations::jsonb
                FROM harvest_work_item w
                WHERE a.work_item_id=@item AND a.attempt=@attempt AND a.finished_at IS NULL
                  AND w.id=a.work_item_id AND w.lease_owner=@owner AND w.lease_until>@now
                """, workerToken, ("operations", Snapshot()), ("item", item.WorkItemId), ("attempt", item.Attempt + 1),
                ("owner", leaseOwner), ("now", clock.GetUtcNow()));
        }
        catch (NpgsqlException error)
        {
            logger.LogWarning("Could not publish operation progress for work item {WorkItemId}. {ErrorType}",
                item.WorkItemId, error.GetType().Name);
        }
    }

    internal static StepOperationResult Failure(Exception error) => error switch
    {
        OperationCanceledException => new("interrupted", Message: "The operation was interrupted."),
        JsonException { LineNumber: long line, BytePositionInLine: long offset } => new("failed",
            Message: $"Invalid JSON at line {line + 1}, byte {offset}."),
        JsonException => new("failed", Message: "The JSON did not match the expected shape."),
        _ => new("failed", Message: error.GetType().Name),
    };
}
