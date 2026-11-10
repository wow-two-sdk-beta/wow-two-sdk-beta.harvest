using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;

/// <summary>Writes <c>harvest_post_revisions</c> inside the transaction that creates a post or raises its revision.</summary>
/// <remarks>The changed field names come from PostgreSQL's own jsonb comparison of the previous and the new <c>base</c>,
/// so key order and spacing never count as a change.</remarks>
internal static class PostRevisionLog
{
    /// <summary>Reads a post's stored base and revision before a write changes them.</summary>
    internal static async Task<(string Base, int Revision)?> ReadAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid containerId, Guid postId, CancellationToken ct)
    {
        await using var command = PostSql.Command(connection, transaction,
            "SELECT base::text,revision FROM harvest_posts WHERE container_id=@container AND id=@post",
            ("container", containerId), ("post", postId));
        await using var reader = await command.ExecuteReaderAsync(ct);
        return await reader.ReadAsync(ct) ? (reader.GetString(0), reader.GetInt32(1)) : null;
    }

    /// <summary>Records the post's current revision when it was just created, or when it rose above
    /// <paramref name="previous"/>; a revision already recorded stays as it is.</summary>
    /// <param name="connection">The open connection.</param>
    /// <param name="transaction">The write's transaction.</param>
    /// <param name="containerId">The post's container.</param>
    /// <param name="postId">The post.</param>
    /// <param name="previous">The base and revision before the write; null for a post the write created.</param>
    /// <param name="now">The write's time.</param>
    /// <param name="ct">The token that cancels the call.</param>
    internal static Task<int> RecordAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid containerId, Guid postId,
        (string Base, int Revision)? previous, DateTimeOffset now, CancellationToken ct) =>
        PostSql.ExecuteAsync(connection, transaction, """
            INSERT INTO harvest_post_revisions(container_id,post_id,revision,at,changed)
            SELECT p.container_id,p.id,p.revision,@now,
                CASE WHEN @created THEN ARRAY[CAST(@createdName AS text)] ELSE ARRAY(
                    SELECT COALESCE(n.key,o.key) FROM jsonb_each(p.base) n
                    FULL JOIN jsonb_each(CAST(@old AS jsonb)) o ON o.key=n.key
                    WHERE NOT (COALESCE(n.key,o.key) = ANY(@ignored))
                      AND COALESCE(n.value,'null'::jsonb) IS DISTINCT FROM COALESCE(o.value,'null'::jsonb)
                    ORDER BY 1) END
            FROM harvest_posts p
            WHERE p.container_id=@container AND p.id=@post AND (@created OR p.revision>@revision)
            ON CONFLICT (post_id,revision) DO NOTHING
            """, ct, ("now", now), ("created", previous is null), ("createdName", PostRevisionConstants.Created),
            ("old", previous?.Base ?? "{}"), ("ignored", PostRevisionConstants.IgnoredFields.ToArray()),
            ("container", containerId), ("post", postId), ("revision", previous?.Revision ?? 0));
}
