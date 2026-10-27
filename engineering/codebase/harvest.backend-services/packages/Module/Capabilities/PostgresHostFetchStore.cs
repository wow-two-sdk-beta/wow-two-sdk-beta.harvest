using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using NpgsqlTypes;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;

/// <summary>Uses a session advisory lock so only one worker connects to a host at a time.</summary>
internal sealed class PostgresHostFetchStore(
    [FromKeyedServices(HarvestModuleConstants.ServiceKey)] NpgsqlDataSource dataSource,
    TimeProvider clock) : IHostFetchStore
{
    public async Task<IHostFetchLease> AcquireAsync(string host, CancellationToken cancellationToken)
    {
        var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        try
        {
            await using (var command = new NpgsqlCommand("SELECT pg_advisory_lock(239017, hashtext($1))", connection))
            {
                command.Parameters.AddWithValue(host);
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await using (var command = new NpgsqlCommand("INSERT INTO harvest_host_state(host_key,next_allowed_at,updated_at) VALUES ($1,$2,$2) ON CONFLICT DO NOTHING", connection))
            {
                command.Parameters.AddWithValue(host);
                command.Parameters.AddWithValue(clock.GetUtcNow());
                await command.ExecuteNonQueryAsync(cancellationToken);
            }
            await using var read = new NpgsqlCommand(
                """
                SELECT next_allowed_at, retry_after_until, paused_until, pause_reason, consecutive_failures,
                       adaptive_delay_ms, robots_body, robots_status, robots_cached_at, robots_origin, permission,
                       COALESCE((SELECT permission FROM harvest_host_policies
                           WHERE host_key=$1 OR right($1, length(host_key)+1)='.' || host_key ORDER BY length(host_key) DESC LIMIT 1), 'open'),
                       min_delay_ms, crawl_delay_s
                FROM harvest_host_state WHERE host_key = $1
                """, connection);
            read.Parameters.AddWithValue(host);
            await using var reader = await read.ExecuteReaderAsync(cancellationToken);
            await reader.ReadAsync(cancellationToken);
            var state = new HostFetchState
            {
                NextAllowedAt = reader.GetFieldValue<DateTimeOffset>(0),
                RetryAfterUntil = reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1),
                PausedUntil = reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
                PauseReason = reader.IsDBNull(3) ? null : reader.GetString(3),
                ConsecutiveFailures = reader.GetInt32(4),
                AdaptiveDelayMilliseconds = reader.GetInt32(5),
                RobotsBody = reader.IsDBNull(6) ? null : reader.GetString(6),
                RobotsStatus = reader.IsDBNull(7) ? null : reader.GetInt16(7),
                RobotsCachedAt = reader.IsDBNull(8) ? null : reader.GetFieldValue<DateTimeOffset>(8),
                RobotsOrigin = reader.IsDBNull(9) ? null : reader.GetString(9),
                Permission = reader.GetString(10),
                PolicyPermission = reader.GetString(11),
                MinimumDelayMilliseconds = reader.GetInt32(12),
                CrawlDelaySeconds = reader.IsDBNull(13) ? null : reader.GetInt32(13)
            };
            return new Lease(connection, host, state);
        }
        catch
        {
            await connection.DisposeAsync();
            throw;
        }
    }

    private sealed class Lease(NpgsqlConnection connection, string host, HostFetchState state) : IHostFetchLease
    {
        public HostFetchState State { get; } = state;

        public async Task SaveAsync(CancellationToken cancellationToken)
        {
            await using var command = new NpgsqlCommand(
                """
                UPDATE harvest_host_state SET next_allowed_at=$2, retry_after_until=$3, paused_until=$4, pause_reason=$5,
                    consecutive_failures=$6, adaptive_delay_ms=$7, robots_body=$8, robots_status=$9, robots_cached_at=$10,
                    robots_origin=$11, crawl_delay_s=$12, updated_at=now() WHERE host_key=$1
                """, connection);
            command.Parameters.AddWithValue(host);
            command.Parameters.AddWithValue(State.NextAllowedAt);
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = (object?)State.RetryAfterUntil ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = (object?)State.PausedUntil ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)State.PauseReason ?? DBNull.Value });
            command.Parameters.AddWithValue(State.ConsecutiveFailures);
            command.Parameters.AddWithValue(State.AdaptiveDelayMilliseconds);
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)State.RobotsBody ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Smallint, Value = State.RobotsStatus is null ? DBNull.Value : (short)State.RobotsStatus });
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = (object?)State.RobotsCachedAt ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)State.RobotsOrigin ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Integer, Value = (object?)State.CrawlDelaySeconds ?? DBNull.Value });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async Task<ResponseValidators?> GetValidatorsAsync(string url, CancellationToken cancellationToken)
        {
            await using var command = new NpgsqlCommand("SELECT etag, last_modified, body, content_type FROM harvest_fetch_validators WHERE url=$1", connection);
            command.Parameters.AddWithValue(url);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            if (!await reader.ReadAsync(cancellationToken))
                return null;
            return new ResponseValidators(reader.IsDBNull(0) ? null : reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetFieldValue<DateTimeOffset>(1),
                reader.IsDBNull(2) ? null : reader.GetFieldValue<byte[]>(2), reader.IsDBNull(3) ? null : reader.GetString(3));
        }

        public async Task SaveValidatorsAsync(string url, ResponseValidators validators, CancellationToken cancellationToken)
        {
            await using var command = new NpgsqlCommand(
                """
                INSERT INTO harvest_fetch_validators(url, etag, last_modified, body, content_type, updated_at) VALUES ($1,$2,$3,$4,$5,now())
                ON CONFLICT(url) DO UPDATE SET etag=EXCLUDED.etag, last_modified=EXCLUDED.last_modified,
                    body=EXCLUDED.body, content_type=EXCLUDED.content_type, updated_at=EXCLUDED.updated_at
                """, connection);
            command.Parameters.AddWithValue(url);
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)validators.ETag ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.TimestampTz, Value = (object?)validators.LastModified ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Bytea, Value = (object?)validators.Body ?? DBNull.Value });
            command.Parameters.Add(new NpgsqlParameter { NpgsqlDbType = NpgsqlDbType.Text, Value = (object?)validators.ContentType ?? DBNull.Value });
            await command.ExecuteNonQueryAsync(cancellationToken);
        }

        public async ValueTask DisposeAsync()
        {
            try
            {
                await using var command = new NpgsqlCommand("SELECT pg_advisory_unlock(239017, hashtext($1))", connection);
                command.Parameters.AddWithValue(host);
                await command.ExecuteNonQueryAsync(CancellationToken.None);
            }
            finally
            {
                await connection.DisposeAsync();
            }
        }
    }
}
