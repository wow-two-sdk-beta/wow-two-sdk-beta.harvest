using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;

/// <summary>Keeps parameterized commands and run projections consistent across queue operations.</summary>
internal static class EngineSql
{
    internal const string RunColumns = "id, container_id, pipeline_id, kind, status, queued_at, started_at, "
        + "finished_at, discovered, captured, unchanged, failed, pages, requests, bytes, fetch_outcome, completion_reason, message";

    internal static NpgsqlCommand Command(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql,
        params (string Name, object? Value)[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        foreach ((string name, object? value) in parameters)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    internal static async Task<int> ExecuteAsync(
        NpgsqlConnection connection,
        NpgsqlTransaction? transaction,
        string sql,
        CancellationToken ct,
        params (string Name, object? Value)[] parameters)
    {
        await using NpgsqlCommand command = Command(connection, transaction, sql, parameters);
        return await command.ExecuteNonQueryAsync(ct);
    }

    internal static RunModel Run(NpgsqlDataReader reader)
    {
        return new RunModel
        {
            Id = reader.GetGuid(0), ContainerId = reader.GetGuid(1), PipelineId = reader.GetGuid(2),
            Kind = SnakeCaseEnum<WorkKind>.Parse(reader.GetString(3)),
            Status = SnakeCaseEnum<RunStatus>.Parse(reader.GetString(4)),
            QueuedAtUtc = reader.GetFieldValue<DateTimeOffset>(5),
            StartedAtUtc = reader.IsDBNull(6) ? null : reader.GetFieldValue<DateTimeOffset>(6),
            FinishedAtUtc = reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
            Discovered = reader.GetInt32(8), Captured = reader.GetInt32(9), Unchanged = reader.GetInt32(10),
            Failed = reader.GetInt32(11), Pages = reader.GetInt32(12), Requests = reader.GetInt32(13), Bytes = reader.GetInt64(14),
            FetchOutcome = reader.IsDBNull(15) ? null : SnakeCaseEnum<FetchOutcome>.Parse(reader.GetString(15)),
            CompletionReason = reader.IsDBNull(16) ? null : SnakeCaseEnum<RunReason>.Parse(reader.GetString(16)),
            Message = reader.IsDBNull(17) ? null : reader.GetString(17),
        };
    }
}
