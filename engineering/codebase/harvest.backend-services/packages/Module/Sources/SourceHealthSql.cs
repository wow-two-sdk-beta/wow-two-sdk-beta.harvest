using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Sources;

/// <summary>Reads host state and refused runs for source health; the overview and the reading-pace write share it.</summary>
internal static class SourceHealthSql
{
    /// <summary>Reads the stored state of hosts, with the worst of their lanes.</summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="hosts">The host keys.</param>
    /// <param name="ct">The token that cancels the call.</param>
    internal static async Task<Dictionary<string, SourceOverviewRules.HostState>> ReadHostsAsync(NpgsqlConnection connection,
        IReadOnlyCollection<string> hosts, CancellationToken ct)
    {
        var states = new Dictionary<string, SourceOverviewRules.HostState>(StringComparer.Ordinal);
        await using var command = PostSql.Command(connection, null, """
            SELECT h.host_key,h.min_delay_ms,h.paused_until,h.pause_reason,h.consecutive_failures,
                l.paused_until,l.reason,COALESCE(l.failures,0)
            FROM harvest_host_state h
            LEFT JOIN LATERAL (
                SELECT max(paused_until) AS paused_until,max(consecutive_failures) AS failures,
                    (array_agg(last_challenge_kind ORDER BY paused_until DESC NULLS LAST))[1] AS reason
                FROM harvest_host_lane WHERE host_key=h.host_key
            ) l ON true
            WHERE h.host_key=ANY(@hosts)
            """, ("hosts", hosts.ToArray()));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            states[reader.GetString(0)] = new SourceOverviewRules.HostState(
                reader.GetInt32(1),
                reader.IsDBNull(2) ? null : reader.GetFieldValue<DateTimeOffset>(2),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetFieldValue<DateTimeOffset>(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetInt32(7));
        return states;
    }

    /// <summary>Reads each source's newest run whose fetch outcome is a refusal or a challenge.</summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="sources">The sources.</param>
    /// <param name="ct">The token that cancels the call.</param>
    internal static async Task<Dictionary<Guid, SourceRefusalModel>> ReadRefusalsAsync(NpgsqlConnection connection,
        IReadOnlyCollection<Guid> sources, CancellationToken ct)
    {
        var refusals = new Dictionary<Guid, SourceRefusalModel>();
        await using var command = PostSql.Command(connection, null, """
            SELECT DISTINCT ON (p.source_id) p.source_id,COALESCE(r.finished_at,r.queued_at),r.fetch_outcome,r.message
            FROM harvest_runs r JOIN harvest_pipelines p ON p.id=r.pipeline_id
            WHERE p.source_id=ANY(@sources) AND r.fetch_outcome=ANY(@outcomes)
            ORDER BY p.source_id,r.queued_at DESC,r.id DESC
            """, ("sources", sources.ToArray()),
            ("outcomes", SourceOverviewRules.Refusals.Select(SnakeCaseEnum<FetchOutcome>.ToText).ToArray()));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            refusals[reader.GetGuid(0)] = new SourceRefusalModel
            {
                At = reader.GetFieldValue<DateTimeOffset>(1),
                Outcome = SnakeCaseEnum<FetchOutcome>.Parse(reader.GetString(2)),
                Message = reader.IsDBNull(3) ? null : reader.GetString(3),
            };
        return refusals;
    }
}
