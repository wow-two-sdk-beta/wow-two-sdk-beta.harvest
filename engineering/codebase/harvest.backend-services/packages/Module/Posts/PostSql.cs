using Npgsql;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;

/// <summary>Executes parameterized commands within a post or delivery transaction.</summary>
internal static class PostSql
{
    /// <summary>Creates a command whose values remain parameters.</summary>
    internal static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, params (string Name, object? Value)[] values)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in values)
            command.Parameters.AddWithValue(name, value ?? DBNull.Value);
        return command;
    }

    /// <summary>Executes a non-query command.</summary>
    internal static async Task<int> ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, CancellationToken ct, params (string Name, object? Value)[] values)
    {
        await using var command = Command(connection, transaction, sql, values);
        return await command.ExecuteNonQueryAsync(ct);
    }

    /// <summary>Executes a scalar command.</summary>
    internal static async Task<object?> ScalarAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, CancellationToken ct, params (string Name, object? Value)[] values)
    {
        await using var command = Command(connection, transaction, sql, values);
        return await command.ExecuteScalarAsync(ct);
    }

    /// <summary>Serializes all post membership and subscription changes in a container.</summary>
    internal static Task<object?> LockAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid containerId, CancellationToken ct) =>
        ScalarAsync(connection, transaction, "SELECT pg_advisory_xact_lock(hashtextextended(CAST(@container AS text),0))", ct, ("container", containerId));
}
