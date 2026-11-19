using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;

/// <summary>Resolves posts into entities and serves the operator's reads and corrections over PostgreSQL.</summary>
internal sealed class EntityBroker(
    [FromKeyedServices(HarvestModuleConstants.ServiceKey)] NpgsqlDataSource dataSource,
    IOptionsMonitor<EntityOptions> options,
    TimeProvider time,
    ILogger<EntityBroker> logger) : IEntityBroker
{
    private const int MaxBackfill = 100_000;
    private const int MaxMergeHops = 10;
    private static readonly TimeSpan MaxRetryDelay = TimeSpan.FromDays(1);

    /// <inheritdoc />
    public async Task<Result<EntityResolveModel>> ResolveAsync(Guid postId, bool dryRun = false, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var outcome = await ResolveAsync(connection, transaction, postId, null, cancellationToken);
        if (outcome is Result<EntityResolveModel>.Success && !dryRun)
            await transaction.CommitAsync(cancellationToken);
        else
            await transaction.RollbackAsync(cancellationToken);
        return outcome is Result<EntityResolveModel>.Success { Value: var resolved } ? Result<EntityResolveModel>.Ok(resolved with { DryRun = dryRun })
            : outcome;
    }

    /// <inheritdoc />
    public async Task<Result<int>> BackfillAsync(EntityBackfillRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (request.Limit is < 1 or > MaxBackfill)
            return Result<int>.Fail(AppErrorFactory.Validation($"The limit must be between 1 and {MaxBackfill}."));
        if (!options.CurrentValue.Enabled)
            return Result<int>.Fail(AppErrorFactory.Conflict($"Enable {EntityConstants.SectionName}:Enabled so the worker resolves what a backfill queues."));
        return Result<int>.Ok(await EntityStore.BackfillAsync(dataSource, request.SinceUtc?.ToUniversalTime(), request.Limit, time.GetUtcNow(),
            cancellationToken));
    }

    /// <inheritdoc />
    public async Task<Result<EntityDetailModel>> GetAsync(Guid entityId, CancellationToken cancellationToken = default)
    {
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        var entity = await EntityStore.ReadEntityAsync(connection, null, entityId, cancellationToken);
        for (int hop = 0; entity?.MergedInto is { } into && hop < MaxMergeHops; hop++)
            entity = await EntityStore.ReadEntityAsync(connection, null, into, cancellationToken);
        if (entity is null)
            return Result<EntityDetailModel>.Fail(AppErrorFactory.NotFound("Entity not found."));
        var occurrences = await EntityStore.ReadOccurrencesAsync(connection, null, entity.Id, cancellationToken);
        var relations = await EntityStore.ReadRelationsAsync(connection, entity.Id, cancellationToken);
        return Result<EntityDetailModel>.Ok(new EntityDetailModel
        {
            Entity = Model(entity),
            RequestedId = entity.Id == entityId ? null : entityId,
            Years = [.. occurrences.GroupBy(row => row.Starts.Year).OrderBy(group => group.Key).Select(group => new EntityYearModel
            {
                Year = group.Key, Occurrences = [.. group.OrderBy(row => row.Starts).Select(Model)],
            })],
            Relations = relations,
        });
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<EntitySearchHitModel>>> SearchAsync(EntitySearchRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        string text = request.Query?.Trim() ?? "";
        if (text.Length is 0 or > 300)
            return Result<IReadOnlyList<EntitySearchHitModel>>.Fail(AppErrorFactory.Validation("The query must be 1 to 300 characters."));
        if (request.Limit is < 1 or > 100)
            return Result<IReadOnlyList<EntitySearchHitModel>>.Fail(AppErrorFactory.Validation("The limit must be between 1 and 100."));
        string? type = request.EntityType?.Trim().ToLowerInvariant();
        if (type is not (null or EntityConstants.EventType or EntityConstants.ListingType))
            return Result<IReadOnlyList<EntitySearchHitModel>>.Fail(AppErrorFactory.Validation("The type is event or listing."));
        string? domain = request.Domain?.Trim().ToLowerInvariant();
        if (domain is not null && !PostExtractionDemand.IsValid(new PostExtractionWant { EntityType = EntityConstants.EventType, Domain = domain }))
            return Result<IReadOnlyList<EntitySearchHitModel>>.Fail(AppErrorFactory.Validation("The domain is a taxonomy key such as style-fashion."));
        string key = EntityNames.Key(text);
        IReadOnlyList<EntitySearchHitModel> hits = await EntityStore.SearchAsync(dataSource, key.Length > 0 ? [key] : [], text, type, domain,
            request.Limit, cancellationToken);
        return Result<IReadOnlyList<EntitySearchHitModel>>.Ok(hits);
    }

    /// <inheritdoc />
    public async Task<Result<EntityMergeModel>> MergeAsync(Guid fromId, Guid intoId, CancellationToken cancellationToken = default)
    {
        if (fromId == intoId)
            return Result<EntityMergeModel>.Fail(AppErrorFactory.Validation("An entity cannot merge into itself."));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var pair = await PairAsync(connection, transaction, fromId, intoId, cancellationToken);
        if (pair is not Result<(EntityRow, EntityRow)>.Success { Value: var (from, into) })
            return Result<EntityMergeModel>.Fail(((Result<(EntityRow, EntityRow)>.Failure)pair).Error);
        if (from.EntityType != into.EntityType)
            return Result<EntityMergeModel>.Fail(AppErrorFactory.Validation("Only entities of one type merge."));
        if (await EntityStore.KeptApartAsync(connection, transaction, from.ContainerId, from.Id, into.Id, cancellationToken))
            return Result<EntityMergeModel>.Fail(AppErrorFactory.Conflict("The entities are kept apart."));

        var now = time.GetUtcNow();
        var settings = options.CurrentValue;
        var resolver = new EntityResolver(connection, transaction, settings, now);
        var fromRows = await EntityStore.ReadOccurrencesAsync(connection, transaction, from.Id, cancellationToken);
        var intoRows = await EntityStore.ReadOccurrencesAsync(connection, transaction, into.Id, cancellationToken);
        var intoBefore = EntityEventLog.Take(into, intoRows);
        var repoint = new List<(Guid From, Guid To)>();
        int moved = 0;
        foreach (var row in fromRows)
        {
            if (row.Predicted)
            {
                row.Deleted = true;
                continue;
            }
            moved++;
            var match = resolver.Match(intoRows, new EventSpan(row.Starts, row.Ends, row.Precision, row.Certainty), EntityNames.Year(row.Label));
            if (match is { Predicted: false })
            {
                Fold(row, match, now);
                repoint.Add((row.Id, match.Id));
                row.Deleted = true;
                continue;
            }
            if (match is { Predicted: true })
            {
                repoint.Add((match.Id, row.Id));
                match.Deleted = true;
            }
            row.EntityId = into.Id;
            row.Dirty = true;
            intoRows.Add(row);
        }
        foreach (var (old, replacement) in repoint)
            await PostSql.ExecuteAsync(connection, transaction, "UPDATE harvest_entity_relations SET occurrence_id=@to WHERE occurrence_id=@from",
                cancellationToken, ("from", old), ("to", replacement));

        int relations = await PostSql.ExecuteAsync(connection, transaction, """
            DELETE FROM harvest_entity_relations WHERE entity_id=@from AND post_id IN (SELECT post_id FROM harvest_entity_relations WHERE entity_id=@into);
            UPDATE harvest_entity_relations SET entity_id=@into WHERE entity_id=@from
            """, cancellationToken, ("from", from.Id), ("into", into.Id));
        await PostSql.ExecuteAsync(connection, transaction, """
            UPDATE harvest_entity_flags SET candidate_id=@into WHERE candidate_id=@from;
            INSERT INTO harvest_entity_keep_apart(container_id,left_entity_id,right_entity_id,created_at)
            SELECT @container,LEAST(@into,other),GREATEST(@into,other),@now FROM (
                SELECT CASE WHEN left_entity_id=@from THEN right_entity_id ELSE left_entity_id END AS other FROM harvest_entity_keep_apart
                WHERE container_id=@container AND (left_entity_id=@from OR right_entity_id=@from)) pairs
            WHERE other<>@into ON CONFLICT DO NOTHING;
            DELETE FROM harvest_entity_keep_apart WHERE container_id=@container AND (left_entity_id=@from OR right_entity_id=@from);
            UPDATE harvest_entities SET merged_into=@into,updated_at=@now WHERE container_id=@container AND merged_into=@from
            """, cancellationToken, ("from", from.Id), ("into", into.Id), ("container", from.ContainerId), ("now", now));

        into.AddAlias(from.Name);
        foreach (var alias in from.Aliases)
            into.AddAlias(alias);
        into.PrimaryDomain ??= from.PrimaryDomain;
        EntityJson.FillMissing(into.Facts, from.Facts);
        into.FirstSeenAt = from.FirstSeenAt < into.FirstSeenAt ? from.FirstSeenAt : into.FirstSeenAt;
        into.LastSeenAt = from.LastSeenAt > into.LastSeenAt ? from.LastSeenAt : into.LastSeenAt;
        if (into.EntityType == EntityConstants.ListingType)
            into.Facts["priceSpread"] = EntityResolver.Spread(await EntityStore.ReadOffersAsync(connection, transaction, into.Id, cancellationToken));
        else
            resolver.Recompute(into, intoRows);
        from.MergedInto = into.Id;

        await EntityStore.SaveOccurrencesAsync(connection, transaction, intoRows.Concat(fromRows).Distinct(), now, cancellationToken);
        await EntityStore.SaveEntityAsync(connection, transaction, into, now, cancellationToken);
        await EntityStore.SaveEntityAsync(connection, transaction, from, now, cancellationToken);
        await EntityEventLog.AppendAsync(connection, transaction,
            [EntityEventLog.Merged(from, into.Id, now), .. EntityEventLog.Diff(intoBefore, into, intoRows, null, now)], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result<EntityMergeModel>.Ok(new EntityMergeModel { FromId = from.Id, IntoId = into.Id, Relations = relations, Occurrences = moved });
    }

    /// <inheritdoc />
    public async Task<Result<EntityKeepApartModel>> SplitAsync(Guid leftId, Guid rightId, CancellationToken cancellationToken = default)
    {
        if (leftId == rightId)
            return Result<EntityKeepApartModel>.Fail(AppErrorFactory.Validation("An entity cannot be kept apart from itself."));
        await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        var pair = await PairAsync(connection, transaction, leftId, rightId, cancellationToken);
        if (pair is not Result<(EntityRow, EntityRow)>.Success { Value: var (left, right) })
            return Result<EntityKeepApartModel>.Fail(((Result<(EntityRow, EntityRow)>.Failure)pair).Error);
        var now = time.GetUtcNow();
        int created = await PostSql.ExecuteAsync(connection, transaction, """
            INSERT INTO harvest_entity_keep_apart(container_id,left_entity_id,right_entity_id,created_at)
            VALUES(@container,LEAST(@left,@right),GREATEST(@left,@right),@now) ON CONFLICT DO NOTHING
            """, cancellationToken, ("container", left.ContainerId), ("left", leftId), ("right", rightId), ("now", now));
        var low = (Guid)(await PostSql.ScalarAsync(connection, transaction, "SELECT LEAST(CAST(@left AS uuid),CAST(@right AS uuid))",
            cancellationToken, ("left", leftId), ("right", rightId)))!;
        if (created > 0)
            await EntityEventLog.AppendAsync(connection, transaction,
                [EntityEventLog.Split(left, right.Id, now), EntityEventLog.Split(right, left.Id, now)], cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return Result<EntityKeepApartModel>.Ok(new EntityKeepApartModel
        {
            LeftId = low, RightId = low == leftId ? rightId : leftId, Created = created > 0,
        });
    }

    /// <inheritdoc />
    public async Task<Result<EntityReportModel>> ReportAsync(CancellationToken cancellationToken = default) =>
        Result<EntityReportModel>.Ok(await EntityStore.CountAsync(dataSource, cancellationToken));

    /// <inheritdoc />
    public async Task<int> ProcessDueAsync(CancellationToken cancellationToken = default)
    {
        var settings = options.CurrentValue;
        string owner = $"{Environment.MachineName}:{Environment.ProcessId}:{Guid.NewGuid():N}";
        var claims = await EntityStore.ClaimAsync(dataSource, owner, time.GetUtcNow(), settings.LeaseDuration, settings.BatchSize, cancellationToken);
        foreach (var claim in claims)
        {
            try
            {
                await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
                await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
                var result = await ResolveAsync(connection, transaction, claim.PostId, claim.ContentHash, cancellationToken);
                await transaction.CommitAsync(cancellationToken);
                if (result is Result<EntityResolveModel>.Failure { Error: var error })
                    logger.LogDebug("Entity resolution of post {PostId} dropped: {Reason}", claim.PostId, error.Message);
                await EntityStore.SettleAsync(dataSource, claim, done: true, null, null, cancellationToken);
            }
            catch (Exception error) when (error is not OperationCanceledException)
            {
                logger.LogError(error, "Entity resolution failed for post {PostId}; it retries later", claim.PostId);
                var retry = time.GetUtcNow() + TimeSpan.FromTicks(Math.Min(MaxRetryDelay.Ticks, settings.RetryDelay.Ticks * Math.Max(1, claim.Attempts)));
                await EntityStore.SettleAsync(dataSource, claim, done: false, retry, error.GetType().Name, cancellationToken);
            }
        }
        return claims.Count;
    }

    /// <summary>Resolves a post inside the caller's transaction; with <paramref name="expectedHash"/>, only while the post's
    /// content still has that hash.</summary>
    private async Task<Result<EntityResolveModel>> ResolveAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid postId,
        byte[]? expectedHash, CancellationToken ct)
    {
        var post = await EntityStore.ReadPostAsync(connection, transaction, postId, ct);
        if (post is null || !post.Current)
            return Result<EntityResolveModel>.Fail(AppErrorFactory.NotFound("Post not found, or merged or purged."));
        if (expectedHash is not null && !expectedHash.AsSpan().SequenceEqual(post.ContentHash))
            return Result<EntityResolveModel>.Fail(AppErrorFactory.Conflict("The post's content moved on since it was queued."));
        await EntityStore.LockAsync(connection, transaction, post.ContainerId, ct);
        var answers = await EntityStore.ReadAnswersAsync(connection, transaction, postId, post.ContentHash, ct);
        if (answers.Count == 0)
            return Result<EntityResolveModel>.Fail(AppErrorFactory.Conflict(
                "Extract the post first: its current title and text have no done extraction answer."));
        var tags = await PostExtractionStore.ReadTagsAsync(connection, transaction, postId, post.ContentHash, ct);
        IReadOnlyList<PostTagsDomain> domains = tags?.Domains ?? [];
        var resolver = new EntityResolver(connection, transaction, options.CurrentValue, time.GetUtcNow());
        var outcomes = new List<EntityResolveOutcome>();
        foreach (var answer in answers)
            outcomes.Add(await resolver.ResolveAsync(post, answer, domains, ct));
        return Result<EntityResolveModel>.Ok(new EntityResolveModel { PostId = postId, Outcomes = outcomes });
    }

    private static async Task<Result<(EntityRow, EntityRow)>> PairAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, Guid leftId,
        Guid rightId, CancellationToken ct)
    {
        var left = await EntityStore.ReadEntityAsync(connection, transaction, leftId, ct);
        var right = await EntityStore.ReadEntityAsync(connection, transaction, rightId, ct);
        if (left is null || right is null)
            return Result<(EntityRow, EntityRow)>.Fail(AppErrorFactory.NotFound("Both entities must exist."));
        if (left.ContainerId != right.ContainerId)
            return Result<(EntityRow, EntityRow)>.Fail(AppErrorFactory.Validation("Both entities must belong to one container."));
        await EntityStore.LockAsync(connection, transaction, left.ContainerId, ct);
        left = (await EntityStore.ReadEntityAsync(connection, transaction, leftId, ct))!;
        right = (await EntityStore.ReadEntityAsync(connection, transaction, rightId, ct))!;
        if (left.MergedInto is not null || right.MergedInto is not null)
            return Result<(EntityRow, EntityRow)>.Fail(AppErrorFactory.Conflict("A merged entity takes no further merge or split."));
        return Result<(EntityRow, EntityRow)>.Ok((left, right));
    }

    /// <summary>Folds an occurrence of the merged entity into the same edition of the one that stays: its history joins,
    /// stated dates replace estimated ones, and a status beyond <c>scheduled</c> carries over.</summary>
    private static void Fold(OccurrenceRow from, OccurrenceRow into, DateTimeOffset now)
    {
        foreach (var entry in from.History)
            into.History.Add(entry?.DeepClone());
        if (into.Certainty == "estimated" && from.Certainty == "stated")
        {
            into.Starts = from.Starts;
            into.Ends = from.Ends;
            into.Precision = from.Precision;
            into.Certainty = from.Certainty;
        }
        if (into.Status is "scheduled" or "unknown" && from.Status is not ("scheduled" or "unknown"))
            into.Status = from.Status;
        into.Place ??= from.Place?.DeepClone() as JsonObject;
        into.History.Add(new JsonObject
        {
            ["field"] = "merged", ["from"] = from.Id.ToString("D"), ["to"] = into.Id.ToString("D"),
            ["at"] = now.ToUniversalTime().ToString("O", System.Globalization.CultureInfo.InvariantCulture),
        });
        into.Dirty = true;
    }

    private static EntityModel Model(EntityRow entity) => new()
    {
        Id = entity.Id, ContainerId = entity.ContainerId, EntityType = entity.EntityType, Name = entity.Name, NameKey = entity.NameKey,
        Aliases = entity.Aliases, PrimaryDomain = entity.PrimaryDomain, Facts = Element(entity.Facts)!.Value, Status = entity.Status,
        Cadence = entity.Cadence, CadenceConfidence = entity.CadenceConfidence, FirstSeenAtUtc = entity.FirstSeenAt,
        LastSeenAtUtc = entity.LastSeenAt, MergedInto = entity.MergedInto,
    };

    private static EntityOccurrenceModel Model(OccurrenceRow row) => new()
    {
        Id = row.Id, Label = row.Label, Starts = row.Starts, Ends = row.Ends, Precision = row.Precision, Certainty = row.Certainty,
        Predicted = row.Predicted, Status = row.Status, Place = Element(row.Place), History = Element(row.History)!.Value,
    };

    private static JsonElement? Element(JsonNode? node) =>
        node is null ? null : JsonDocument.Parse(node.ToJsonString()).RootElement.Clone();
}
