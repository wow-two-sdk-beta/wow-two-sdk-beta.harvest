using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;

internal sealed partial class ModulePostBroker
{
    /// <summary>Queues Layer 2 extraction for a post's content once its tags are done: the use case of the tagged entity
    /// type, when the demand gate wants a pair the tags match and the content has no done answer for it.</summary>
    /// <remarks>Runs inside the write's transaction; a host without AI or demand skips it before any query.</remarks>
    private async Task QueueExtractionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid containerId, Guid postId,
        byte[] contentHash, DateTimeOffset now, CancellationToken ct)
    {
        if (!extraction.Enabled)
            return;
        var wanted = await extraction.WantedAsync(ct);
        if (wanted.Count == 0 || await PostExtractionStore.ReadTagsAsync(connection, transaction, postId, contentHash, ct) is not { } tags)
            return;
        if (PostExtractionDemand.Match(wanted, tags.EntityType, tags.Domains) is { } useCase)
            await PostExtractionStore.QueueAsync(connection, transaction, containerId, postId, contentHash, useCase, now, ct);
    }
}
