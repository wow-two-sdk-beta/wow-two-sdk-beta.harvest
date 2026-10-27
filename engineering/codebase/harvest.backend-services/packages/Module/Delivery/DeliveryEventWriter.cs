using System.Security.Cryptography;
using System.Text;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;

/// <summary>Appends a post revision to interested subscriptions inside the post writer's transaction.</summary>
internal static class DeliveryEventWriter
{
    internal static async Task AppendAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, PostModel post, string type, Guid? runId, Guid? linkId, DateTimeOffset at, CancellationToken ct)
    {
        var subscriptions = new List<Guid>();
        await using (var command = PostSql.Command(connection, transaction, """
                         SELECT s.id FROM harvest_subscriptions s JOIN harvest_pipelines p ON p.id=s.pipeline_id
                         WHERE s.container_id=@container AND s.state='active' AND (
                             (CAST(@run AS uuid) IS NOT NULL AND p.id=(SELECT pipeline_id FROM harvest_runs WHERE id=@run))
                             OR (CAST(@run AS uuid) IS NULL AND EXISTS(SELECT 1 FROM harvest_post_links l WHERE
                                 (l.post_id=@post OR l.post_id=CAST(@merged AS uuid)) AND l.source_id=p.source_id AND l.retired_at IS NULL))
                             OR EXISTS(SELECT 1 FROM harvest_subscription_event e WHERE e.subscription_id=s.id AND e.post_id=@post))
                         """, ("container", post.ContainerId), ("run", runId), ("post", post.Id), ("merged", post.MergedInto)))
        await using (var reader = await command.ExecuteReaderAsync(ct))
            while (await reader.ReadAsync(ct)) subscriptions.Add(reader.GetGuid(0));
        foreach (var subscriptionId in subscriptions)
            await AppendOneAsync(connection, transaction, subscriptionId, post, type, runId, linkId, "ingest", at, ct);
    }

    internal static Task<int> AppendOneAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid subscriptionId, PostModel post, string type, Guid? runId, Guid? linkId, string reason, DateTimeOffset at, CancellationToken ct) =>
        PostSql.ExecuteAsync(connection, transaction, """
            INSERT INTO harvest_subscription_event(subscription_id,event_id,item_id,type,post_id,post_key,revision,link_id,run_id,reason,occurred_at,payload)
            VALUES(@subscription,@event,@item,@type,@post,@key,@revision,@link,@run,@reason,@at,CAST(@payload AS jsonb))
            ON CONFLICT(subscription_id,item_id,type) DO NOTHING
            """, ct, ("subscription", subscriptionId), ("event", Guid.CreateVersion7(at)),
            ("item", Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes($"{post.Id:N}:{post.Revision}:{type}"))).ToLowerInvariant()),
            ("type", type), ("post", post.Id), ("key", post.Id.ToString("N")), ("revision", post.Revision), ("link", linkId),
            ("run", runId), ("reason", reason), ("at", at), ("payload", StoreJson.Write(post)));
}
