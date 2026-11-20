using System.Text.Json;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;

/// <summary>Fills the <c>fileRef</c> of each image in a rendered event payload's <c>media[]</c> with the id of the Files copy
/// SCFD holds for that image's source URL. The source <c>url</c> stays beside it as the credit link.</summary>
/// <remarks>The copy is made by the source-asset worker after the post is stored, so the lookup runs when a page is pulled
/// and reads <c>harvest_source_assets</c> through the sources the post is linked from; an image with no copy keeps its null.</remarks>
internal static class DeliveryMediaFiles
{
    private const string FileRefKey = "fileRef";

    /// <summary>Reads the copied file of every (post, image URL) the events' payloads name.</summary>
    /// <param name="connection">An open connection.</param>
    /// <param name="subscriptionId">The subscription whose container holds the posts.</param>
    /// <param name="events">The events of one page.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The copied file by post and URL; empty when no image has a copy.</returns>
    public static async Task<IReadOnlyDictionary<(Guid PostId, string Url), Guid>> ReadAsync(
        NpgsqlConnection connection, Guid subscriptionId, IEnumerable<DeliveryEvent> events, CancellationToken ct)
    {
        var posts = new HashSet<Guid>();
        var urls = new HashSet<string>(StringComparer.Ordinal);
        foreach (var item in events)
            if (item is { PostId: { } post, Data: { } data } && Pending(data).ToList() is { Count: > 0 } wanted)
            {
                posts.Add(post);
                urls.UnionWith(wanted);
            }
        var files = new Dictionary<(Guid, string), Guid>();
        if (posts.Count == 0) return files;
        await using var command = PostSql.Command(connection, null, """
            SELECT DISTINCT ON (l.post_id,a.origin_url) l.post_id,a.origin_url,a.file_id
            FROM harvest_subscriptions s
            JOIN harvest_post_links l ON l.container_id=s.container_id AND l.post_id=ANY(@posts)
            JOIN harvest_source_assets a ON a.source_id=l.source_id AND a.kind='image' AND a.origin_url=ANY(@urls) AND a.file_id IS NOT NULL
            WHERE s.id=@subscription
            ORDER BY l.post_id,a.origin_url,a.file_id
            """, ("subscription", subscriptionId), ("posts", posts.ToArray()), ("urls", urls.ToArray()));
        await using var reader = await command.ExecuteReaderAsync(ct);
        while (await reader.ReadAsync(ct))
            files[(reader.GetGuid(0), reader.GetString(1))] = reader.GetGuid(2);
        return files;
    }

    /// <summary>Returns the payload with the copied file's id on each image that has one, or null when none changes.</summary>
    /// <param name="data">The rendered payload.</param>
    /// <param name="postId">The post the payload renders.</param>
    /// <param name="files">The copied files by post and URL.</param>
    public static JsonElement? Apply(JsonElement data, Guid postId, IReadOnlyDictionary<(Guid PostId, string Url), Guid> files)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Array)
            return null;
        var changed = false;
        var items = new List<Dictionary<string, JsonElement>>();
        foreach (var item in media.EnumerateArray())
        {
            if (item.ValueKind != JsonValueKind.Object)
                return null;
            var values = item.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone());
            if (IsPending(item, out var url) && files.TryGetValue((postId, url), out var file))
            {
                values[FileRefKey] = JsonSerializer.SerializeToElement(file.ToString("D"));
                changed = true;
            }
            items.Add(values);
        }
        if (!changed) return null;
        var rebuilt = data.EnumerateObject().ToDictionary(property => property.Name, property => property.Value.Clone());
        rebuilt["media"] = JsonSerializer.SerializeToElement(items);
        return JsonSerializer.SerializeToElement(rebuilt);
    }

    /// <summary>Lists the source URLs of the payload's images that carry no stored copy yet.</summary>
    private static IEnumerable<string> Pending(JsonElement data)
    {
        if (data.ValueKind != JsonValueKind.Object || !data.TryGetProperty("media", out var media) || media.ValueKind != JsonValueKind.Array)
            yield break;
        foreach (var item in media.EnumerateArray())
            if (item.ValueKind == JsonValueKind.Object && IsPending(item, out var url))
                yield return url;
    }

    /// <summary>Whether the item is an image with a source URL and no copy named yet.</summary>
    private static bool IsPending(JsonElement item, out string url)
    {
        url = string.Empty;
        if (!item.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String
            || !string.Equals(kind.GetString(), "image", StringComparison.OrdinalIgnoreCase)
            || !item.TryGetProperty("url", out var source) || source.ValueKind != JsonValueKind.String
            || source.GetString() is not { Length: > 0 } text
            || item.TryGetProperty(FileRefKey, out var existing) && existing.ValueKind is not JsonValueKind.Null)
            return false;
        url = text;
        return true;
    }
}
