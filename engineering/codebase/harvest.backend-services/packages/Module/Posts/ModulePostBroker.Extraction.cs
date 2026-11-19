using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Reprocessing;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;

internal sealed partial class ModulePostBroker
{
    /// <summary>Stores an extraction answer and, for a done <c>extract-event</c> answer about the post's current title and
    /// text, resolves the post again so the answer's kind and estimated dates become a new revision with
    /// <c>post.changed</c>; all in one transaction.</summary>
    /// <remarks>An answer that changes nothing on the post — a listing, the same kind, a stated start — raises no
    /// revision.</remarks>
    /// <returns>The revision the answer raised, or null.</returns>
    internal async Task<int?> WriteExtractionAsync(PostExtractionRecord record, bool queueEntities, CancellationToken ct)
    {
        await using var connection = await dataSource.OpenConnectionAsync(ct);
        await using var transaction = await connection.BeginTransactionAsync(ct);
        await PostSql.LockAsync(connection, transaction, record.ContainerId, ct);
        var now = Now();
        await PostExtractionStore.WriteAsync(connection, transaction, record, now, queueEntities, ct);
        int? revised = null;
        if (record is { Status: "done", Answer: not null, UseCase: PostExtractionConstants.EventUseCase }
            && await ReadPostAsync(connection, transaction, record.ContainerId, record.PostId, ct) is { MergedInto: null } post
            && PostTagsContent.Hash(post.Base).AsSpan().SequenceEqual(record.ContentHash)
            && await ResolveAsync(connection, transaction, record.ContainerId, record.PostId, now, true, ct, onlyIfChanged: true))
        {
            var updated = (await ReadPostAsync(connection, transaction, record.ContainerId, record.PostId, ct))!;
            await DeliveryEventWriter.AppendAsync(connection, transaction, updated, "post.changed", null, null, now, postFilters, ct);
            revised = updated.Revision;
        }
        await transaction.CommitAsync(ct);
        return revised;
    }

    /// <summary>Queues Layer 2 extraction for a post's content once its tags are done: the use case of the tagged entity
    /// type, when the demand gate wants a pair the tags match and the content has no done answer for it.</summary>
    /// <remarks>Runs inside the write's transaction; a host without AI or demand skips it before any query. Tags an
    /// operator asked for (<paramref name="explicitRow"/>) queue extraction under a <c>manual</c> source too.</remarks>
    private async Task QueueExtractionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid containerId, Guid postId,
        byte[] contentHash, DateTimeOffset now, CancellationToken ct, bool explicitRow = false)
    {
        if (!extraction.Enabled)
            return;
        var wanted = await extraction.WantedAsync(ct);
        if (wanted.Count == 0 || await PostExtractionStore.ReadTagsAsync(connection, transaction, postId, contentHash, ct) is not { } tags)
            return;
        if (PostExtractionDemand.Match(wanted, tags.EntityType, tags.Domains) is { } useCase)
            await PostExtractionStore.QueueAsync(connection, transaction, containerId, postId, contentHash, useCase, now, ct,
                explicitRow: explicitRow);
    }

    /// <summary>Queues the extraction a re-run's done tags lead to, carrying the run: forced (a done answer is replaced)
    /// when the run asked for the tagged type's use case, or when the tags extraction reads changed and the demand wants
    /// the pair; otherwise as after any tags.</summary>
    /// <remarks>A run over listed or all sources asks only for what the demand wants; one over named posts for any post
    /// tagged with the use case's entity type.</remarks>
    private async Task QueueRerunExtractionAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, PostTagsRecord record, Guid runId,
        bool changed, DateTimeOffset now, CancellationToken ct)
    {
        if (!extraction.Enabled
            || await PostExtractionStore.ReadTagsAsync(connection, transaction, record.PostId, record.ContentHash, ct) is not { } tags
            || PostExtractionConstants.UseCaseFor(tags.EntityType) is not { } useCase)
            return;
        var wanted = await extraction.WantedAsync(ct);
        bool demanded = PostExtractionDemand.Match(wanted, tags.EntityType, tags.Domains) == useCase;
        var (asked, named) = await ReprocessingStore.AskedAsync(connection, transaction, runId, ct);
        if (asked.Contains(useCase) && (named || demanded) || changed && demanded)
            await PostExtractionStore.QueueAsync(connection, transaction, record.ContainerId, record.PostId, record.ContentHash, useCase, now, ct,
                runId, record.Explicit, force: true);
        else if (demanded)
            await PostExtractionStore.QueueAsync(connection, transaction, record.ContainerId, record.PostId, record.ContentHash, useCase, now, ct,
                runId, record.Explicit);
    }
}
