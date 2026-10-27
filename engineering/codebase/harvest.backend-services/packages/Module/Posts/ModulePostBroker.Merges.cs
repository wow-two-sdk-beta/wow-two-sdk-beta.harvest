using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;

internal sealed partial class ModulePostBroker
{
    /// <inheritdoc />
    public async Task<Result<PostMergeModel>> MergeAsync(Guid containerId, Guid survivorId, Guid loserId, CancellationToken cancellationToken = default)
    {
        if (survivorId == loserId) return Invalid<PostMergeModel>("A post cannot merge into itself.");
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostSql.LockAsync(connection, transaction, containerId, cancellationToken);
        var survivor = await ReadPostAsync(connection, transaction, containerId, survivorId, cancellationToken);
        var loser = await ReadPostAsync(connection, transaction, containerId, loserId, cancellationToken);
        if (survivor is null || loser is null) return Missing<PostMergeModel>("Both posts must belong to the specified container.");
        if (survivor.MergedInto is not null || loser.MergedInto is not null)
            return Conflict<PostMergeModel>("A merged post must be unmerged before another merge.");
        if (await PostSql.ScalarAsync(connection, transaction, """
                SELECT 1 FROM harvest_post_merges WHERE container_id=@container AND undone_at IS NULL
                AND (survivor_id=ANY(@posts) OR loser_id=ANY(@posts)) LIMIT 1
                """, cancellationToken, ("container", containerId), ("posts", new[] { survivorId, loserId })) is not null)
            return Conflict<PostMergeModel>("Undo the existing merge before changing either post's membership again.");
        var now = Now();
        var mergeId = Guid.CreateVersion7(now);
        await PostSql.ExecuteAsync(connection, transaction, """
            INSERT INTO harvest_post_merges(id,container_id,survivor_id,loser_id,link_ids,created_at)
            SELECT @merge,@container,@survivor,@loser,array_agg(id),@now FROM harvest_post_links WHERE container_id=@container AND post_id=@loser;
            UPDATE harvest_post_links SET post_id=@survivor WHERE container_id=@container AND post_id=@loser;
            UPDATE harvest_posts SET merged_into=@survivor,revision=revision+1,last_changed_at=@now WHERE container_id=@container AND id=@loser;
            DELETE FROM harvest_post_keep_apart WHERE container_id=@container AND left_post_id=LEAST(@survivor,@loser) AND right_post_id=GREATEST(@survivor,@loser)
            """, cancellationToken, ("merge", mergeId), ("container", containerId), ("survivor", survivorId), ("loser", loserId), ("now", now));
        await ResolveAsync(connection, transaction, containerId, survivorId, now, true, cancellationToken);
        var merged = (await ReadPostAsync(connection, transaction, containerId, loserId, cancellationToken))!;
        var resolved = (await ReadPostAsync(connection, transaction, containerId, survivorId, cancellationToken))!;
        await DeliveryEventWriter.AppendAsync(connection, transaction, merged, "post.merged", null, null, now, postFilters, cancellationToken);
        await DeliveryEventWriter.AppendAsync(connection, transaction, resolved, "post.changed", null, null, now, postFilters, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result<PostMergeModel>.Ok(new PostMergeModel { Id = mergeId, SurvivorId = survivorId, LoserId = loserId, Undone = false });
    }

    /// <inheritdoc />
    public async Task<Result<PostMergeModel>> UnmergeAsync(Guid containerId, Guid mergeId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await PostSql.LockAsync(connection, transaction, containerId, cancellationToken);
        Guid survivorId, loserId;
        Guid[] links;
        bool undone;
        await using (var command = PostSql.Command(connection, transaction,
                         "SELECT survivor_id,loser_id,link_ids,undone_at IS NOT NULL FROM harvest_post_merges WHERE container_id=@container AND id=@id",
                         ("container", containerId), ("id", mergeId)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
        {
            if (!await reader.ReadAsync(cancellationToken)) return Missing<PostMergeModel>("Merge not found in the container.");
            survivorId = reader.GetGuid(0); loserId = reader.GetGuid(1); links = reader.GetFieldValue<Guid[]>(2); undone = reader.GetBoolean(3);
        }
        if (undone)
        {
            await transaction.CommitAsync(cancellationToken);
            return Result<PostMergeModel>.Ok(new PostMergeModel { Id = mergeId, SurvivorId = survivorId, LoserId = loserId, Undone = true });
        }
        var now = Now();
        await PostSql.ExecuteAsync(connection, transaction, """
            UPDATE harvest_post_links SET post_id=@loser WHERE container_id=@container AND id=ANY(@links);
            UPDATE harvest_posts SET merged_into=NULL WHERE container_id=@container AND id=@loser;
            UPDATE harvest_post_merges SET undone_at=@now WHERE id=@merge;
            INSERT INTO harvest_post_keep_apart(container_id,left_post_id,right_post_id,created_at)
            VALUES(@container,LEAST(@survivor,@loser),GREATEST(@survivor,@loser),@now) ON CONFLICT DO NOTHING
            """, cancellationToken, ("loser", loserId), ("container", containerId), ("links", links), ("now", now), ("merge", mergeId), ("survivor", survivorId));
        await ResolveAsync(connection, transaction, containerId, survivorId, now, true, cancellationToken);
        await ResolveAsync(connection, transaction, containerId, loserId, now, true, cancellationToken);
        var survivor = (await ReadPostAsync(connection, transaction, containerId, survivorId, cancellationToken))!;
        var loser = (await ReadPostAsync(connection, transaction, containerId, loserId, cancellationToken))!;
        await DeliveryEventWriter.AppendAsync(connection, transaction, survivor, "post.unmerged", null, null, now, postFilters, cancellationToken);
        await DeliveryEventWriter.AppendAsync(connection, transaction, loser, "post.unmerged", null, null, now, postFilters, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result<PostMergeModel>.Ok(new PostMergeModel { Id = mergeId, SurvivorId = survivorId, LoserId = loserId, Undone = true });
    }
}
