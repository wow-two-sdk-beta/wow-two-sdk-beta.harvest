using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;

internal sealed partial class ModulePostBroker
{
    private static readonly HashSet<string> ObservationReasons =
    [
        "direct_410", "direct_404", "named_redirect", "soft_404", "node_vanished", "absent_from_enumeration", "embed_not_found",
        "mtproto_empty", "delete_update", "provider_error_item", "compliance_delete", "api_missing", "seen_again", "manual",
    ];

    /// <inheritdoc />
    public async Task<Result<PostModel>> ObserveLinkAsync(Guid containerId, Guid sourceId, LinkObservation observation, Guid? runId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(observation.ExternalKey) || !ObservationReasons.Contains(observation.Reason) || !Enum.IsDefined(observation.Signal))
            return Invalid<PostModel>("The observation requires a source-local key, a supported reason and a valid signal.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostSql.LockAsync(connection, transaction, containerId, cancellationToken);
        var link = await FindLinkAsync(connection, transaction, containerId, sourceId, observation.ExternalKey, cancellationToken);
        if (link is null) return Missing<PostModel>("Link not found in the source and container.");
        if (runId is not null && await PostSql.ScalarAsync(connection, transaction,
                "SELECT 1 FROM harvest_runs r JOIN harvest_pipelines p ON p.id=r.pipeline_id WHERE r.container_id=@container AND r.id=@run AND p.source_id=@source", cancellationToken,
                ("container", containerId), ("run", runId), ("source", sourceId)) is null)
            return Invalid<PostModel>("The observation's run must belong to its source and container.");
        var now = Now();
        var checkable = await PostSql.ScalarAsync(connection, transaction, """
            SELECT 1 FROM harvest_post_links l JOIN harvest_sources s ON s.id=l.source_id
            WHERE l.id=@link AND l.check_capability<>'none' AND l.retired_at IS NULL AND s.state='active' AND s.permission IN ('open','granted')
            """, cancellationToken, ("link", link.Id)) is not null;
        if (!checkable && observation.Signal != LinkSignal.Present)
        {
            var abstained = (await ReadPostAsync(connection, transaction, containerId, link.PostId, cancellationToken))!;
            await transaction.CommitAsync(cancellationToken);
            return Result<PostModel>.Ok(abstained);
        }
        var logged = await PostSql.ExecuteAsync(connection, transaction, """
            INSERT INTO harvest_link_check_log(id,container_id,link_id,checked_at,method,outcome,http_status,run_id,evidence)
            VALUES(@id,@container,@link,@now,'direct_fetch',@outcome,@status,@run,jsonb_build_object('reason',CAST(@reason AS text)))
            ON CONFLICT(link_id,run_id) WHERE run_id IS NOT NULL DO NOTHING
            """, cancellationToken, ("id", Guid.CreateVersion7(now)), ("container", containerId), ("link", link.Id),
            ("now", now), ("outcome", observation.Signal.ToString()), ("status", observation.HttpStatus is { } status ? (short)status : null),
            ("run", runId), ("reason", observation.Reason));
        if (logged == 0)
        {
            var replay = (await ReadPostAsync(connection, transaction, containerId, link.PostId, cancellationToken))!;
            await transaction.CommitAsync(cancellationToken);
            return Result<PostModel>.Ok(replay);
        }
        var missing = Convert.ToInt32(await PostSql.ScalarAsync(connection, transaction, """
            UPDATE harvest_link_schedule SET last_checked_at=@now,check_count=check_count+1,
                consecutive_missing=CASE WHEN @present THEN 0 ELSE consecutive_missing+1 END,last_outcome=@outcome,
                last_seen_at=CASE WHEN @present THEN @now ELSE last_seen_at END,next_check_at=@next,due=true
            WHERE link_id=@link RETURNING consecutive_missing
            """, cancellationToken, ("now", now), ("present", observation.Signal == LinkSignal.Present),
            ("outcome", observation.Signal.ToString()), ("next", now.AddHours(6)), ("link", link.Id)));
        var next = observation.Signal switch
        {
            LinkSignal.Present when link.State == LinkState.Gone => LinkState.Relisted,
            LinkSignal.Present => LinkState.Live,
            LinkSignal.StrongNegative when observation.Reason is "direct_410" or "compliance_delete" or "delete_update" => LinkState.Gone,
            LinkSignal.StrongNegative when missing >= 2 => LinkState.Gone,
            _ when link.State == LinkState.Gone => LinkState.Gone,
            _ => LinkState.Suspect,
        };
        if (next != link.State)
        {
            await SetLinkStateAsync(connection, transaction, containerId, link, next, observation.Reason, runId, now, cancellationToken);
            await ResolveAsync(connection, transaction, containerId, link.PostId, now, true, cancellationToken);
            var changed = (await ReadPostAsync(connection, transaction, containerId, link.PostId, cancellationToken))!;
            await DeliveryEventWriter.AppendAsync(connection, transaction, changed, "link.state", runId, link.Id, now, cancellationToken);
            await transaction.CommitAsync(cancellationToken);
            return Result<PostModel>.Ok(changed);
        }
        var post = (await ReadPostAsync(connection, transaction, containerId, link.PostId, cancellationToken))!;
        await transaction.CommitAsync(cancellationToken);
        return Result<PostModel>.Ok(post);
    }

    private static Task<int> SetLinkStateAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid containerId, LinkRow link, LinkState next, string reason, Guid? runId, DateTimeOffset now, CancellationToken ct) =>
        PostSql.ExecuteAsync(connection, transaction, """
            UPDATE harvest_post_links SET state=@state,state_since=@now,last_changed_at=@now,
                gone_at=CASE WHEN @state='gone' THEN @now ELSE NULL END,
                relist_count=relist_count+CASE WHEN @state='relisted' THEN 1 ELSE 0 END WHERE id=@link;
            INSERT INTO harvest_link_events(id,container_id,link_id,at,from_state,to_state,reason,run_id)
            VALUES(@id,@container,@link,@now,@from,@state,@reason,@run)
            """, ct, ("state", next.ToString().ToLowerInvariant()), ("now", now), ("link", link.Id),
            ("id", Guid.CreateVersion7(now)), ("container", containerId), ("from", link.State.ToString().ToLowerInvariant()),
            ("reason", reason), ("run", runId));
}
