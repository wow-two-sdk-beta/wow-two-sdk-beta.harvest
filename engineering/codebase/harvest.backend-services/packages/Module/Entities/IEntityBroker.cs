using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;

/// <summary>Runs the entity layer: binds each extracted post, with a role, to the one real-world event or listing it is
/// about, keeps events' editions with a cadence and a predicted next run and listings' offers across authors and sites,
/// and lets an operator read, merge and split entities. It never writes the post.</summary>
public interface IEntityBroker
{
    /// <summary>Resolves a post's done extraction answers for its current title and text now, whatever the queue holds;
    /// a dry run decides and writes nothing.</summary>
    /// <param name="postId">The post.</param>
    /// <param name="dryRun">Whether to roll every write back.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The outcome per use case; a failure when the post does not exist, is merged or purged, or has no done
    /// extraction answer for its current content.</returns>
    Task<Result<EntityResolveModel>> ResolveAsync(Guid postId, bool dryRun = false, CancellationToken cancellationToken = default);

    /// <summary>Queues stored posts whose current content has a done extraction answer and no relation or flag yet: the
    /// backfill for answers stored before the layer was enabled.</summary>
    /// <param name="request">Which posts.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The number of resolutions queued or re-armed; a failure while the layer is disabled.</returns>
    Task<Result<int>> BackfillAsync(EntityBackfillRequest request, CancellationToken cancellationToken = default);

    /// <summary>Reads an entity with its occurrences by year and every relation; a merged entity reads as the one it was
    /// merged into.</summary>
    /// <param name="entityId">The entity.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    Task<Result<EntityDetailModel>> GetAsync(Guid entityId, CancellationToken cancellationToken = default);

    /// <summary>Finds entities by name and alias similarity, with the years they ran.</summary>
    /// <param name="request">The text and filters.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    Task<Result<IReadOnlyList<EntitySearchHitModel>>> SearchAsync(EntitySearchRequest request, CancellationToken cancellationToken = default);

    /// <summary>Merges one entity into another of the same container and type: relations, occurrences, aliases and
    /// facts move, and the cadence is inferred again.</summary>
    /// <param name="fromId">The entity that stops existing on its own.</param>
    /// <param name="intoId">The entity that stays.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The merge; a conflict when either is merged already or the pair is kept apart.</returns>
    Task<Result<EntityMergeModel>> MergeAsync(Guid fromId, Guid intoId, CancellationToken cancellationToken = default);

    /// <summary>Records that two entities are different things: resolution never binds a post across the pair and a
    /// merge of the pair is refused.</summary>
    /// <param name="leftId">One entity.</param>
    /// <param name="rightId">The other, in the same container.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    Task<Result<EntityKeepApartModel>> SplitAsync(Guid leftId, Guid rightId, CancellationToken cancellationToken = default);

    /// <summary>Counts entities by type, cadence and status, occurrences by status, relations by role, open unsure flags
    /// and the queue; never post text.</summary>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    Task<Result<EntityReportModel>> ReportAsync(CancellationToken cancellationToken = default);

    /// <summary>Claims due queued resolutions and runs each; the worker calls it on every tick.</summary>
    /// <param name="cancellationToken">The token that cancels the pass.</param>
    /// <returns>The number of resolutions claimed.</returns>
    Task<int> ProcessDueAsync(CancellationToken cancellationToken = default);
}

/// <summary>Represents which stored posts a backfill queues.</summary>
public sealed record EntityBackfillRequest
{
    /// <summary>Gets the earliest extraction answer to include; every answer when omitted.</summary>
    public DateTimeOffset? SinceUtc { get; init; }

    /// <summary>Gets the most resolutions to queue, newest answer first: 1 to 100,000.</summary>
    public int Limit { get; init; } = 1000;
}

/// <summary>Represents an entity search.</summary>
public sealed record EntitySearchRequest
{
    /// <summary>Gets the name to search for, in any of the sources' languages.</summary>
    public required string Query { get; init; }

    /// <summary>Gets the entity type to keep, <c>event</c> or <c>listing</c>; both when omitted.</summary>
    public string? EntityType { get; init; }

    /// <summary>Gets the taxonomy key the primary domain must equal or sit under, such as <c>style-fashion</c>.</summary>
    public string? Domain { get; init; }

    /// <summary>Gets the most entities to return: 1 to 100.</summary>
    public int Limit { get; init; } = 20;
}

/// <summary>Represents one resolution of a post.</summary>
public sealed record EntityResolveModel
{
    /// <summary>Gets the post.</summary>
    public required Guid PostId { get; init; }

    /// <summary>Gets whether every write was rolled back.</summary>
    public bool DryRun { get; init; }

    /// <summary>Gets the outcome of each done extraction answer, event first.</summary>
    public required IReadOnlyList<EntityResolveOutcome> Outcomes { get; init; }
}

/// <summary>Represents what resolving one extraction answer did.</summary>
public sealed record EntityResolveOutcome
{
    /// <summary>Gets the extraction use case: <c>extract-event</c> or <c>extract-listing</c>.</summary>
    public required string UseCase { get; init; }

    /// <summary>Gets the outcome: <c>created</c> a new entity, <c>bound</c> to an existing one, or <c>unsure</c> with a
    /// flag and no binding.</summary>
    public required string Outcome { get; init; }

    /// <summary>Gets the entity bound to, or the best candidate of an unsure outcome.</summary>
    public Guid? EntityId { get; init; }

    /// <summary>Gets the occurrence the relation points at, for an event.</summary>
    public Guid? OccurrenceId { get; init; }

    /// <summary>Gets the relation's role.</summary>
    public string? Role { get; init; }

    /// <summary>Gets what an update changed.</summary>
    public IReadOnlyList<string> Changes { get; init; } = [];

    /// <summary>Gets the match confidence, 0 to 1; 1 for a created entity.</summary>
    public decimal? Confidence { get; init; }

    /// <summary>Gets why an outcome is unsure: <c>weak-match</c>, <c>ambiguous</c> or <c>no-name</c>.</summary>
    public string? Reason { get; init; }
}

/// <summary>Represents an entity as stored.</summary>
public sealed record EntityModel
{
    /// <summary>Gets the entity's id.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets the container it belongs to.</summary>
    public required Guid ContainerId { get; init; }

    /// <summary>Gets its type: <c>event</c> or <c>listing</c>.</summary>
    public required string EntityType { get; init; }

    /// <summary>Gets its canonical English name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the normalized key matching compares.</summary>
    public required string NameKey { get; init; }

    /// <summary>Gets the other names sources gave it, in their languages.</summary>
    public IReadOnlyList<string> Aliases { get; init; } = [];

    /// <summary>Gets its primary taxonomy node.</summary>
    public string? PrimaryDomain { get; init; }

    /// <summary>Gets its merged canonical facts: no evidence quotes, no post text.</summary>
    public JsonElement Facts { get; init; }

    /// <summary>Gets its status: <c>active</c>, or <c>sold</c> or <c>withdrawn</c> for a listing.</summary>
    public required string Status { get; init; }

    /// <summary>Gets how often it runs: <c>annual</c>, <c>semiannual</c>, <c>quarterly</c>, <c>monthly</c>,
    /// <c>irregular</c> or <c>unknown</c>.</summary>
    public required string Cadence { get; init; }

    /// <summary>Gets how sure the cadence is, 0 to 1.</summary>
    public decimal CadenceConfidence { get; init; }

    /// <summary>Gets when a post first bound to it.</summary>
    public required DateTimeOffset FirstSeenAtUtc { get; init; }

    /// <summary>Gets when a post last bound to it.</summary>
    public required DateTimeOffset LastSeenAtUtc { get; init; }

    /// <summary>Gets the entity it was merged into.</summary>
    public Guid? MergedInto { get; init; }
}

/// <summary>Represents one run of an event.</summary>
public sealed record EntityOccurrenceModel
{
    /// <summary>Gets the occurrence's id.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets its label: the edition label a source gave, else the year it starts.</summary>
    public required string Label { get; init; }

    /// <summary>Gets its first day.</summary>
    public required DateOnly Starts { get; init; }

    /// <summary>Gets its last day.</summary>
    public DateOnly? Ends { get; init; }

    /// <summary>Gets the finest unit its dates state: <c>year</c>, <c>month</c>, <c>day</c> or finer.</summary>
    public required string Precision { get; init; }

    /// <summary>Gets whether a source stated its dates or they are estimated.</summary>
    public required string Certainty { get; init; }

    /// <summary>Gets whether the cadence predicted it and no source has announced it yet.</summary>
    public bool Predicted { get; init; }

    /// <summary>Gets its status: <c>scheduled</c>, <c>cancelled</c>, <c>postponed</c>, <c>held</c> or <c>unknown</c>.</summary>
    public required string Status { get; init; }

    /// <summary>Gets where it takes place.</summary>
    public JsonElement? Place { get; init; }

    /// <summary>Gets every prior value with the post that changed it.</summary>
    public JsonElement History { get; init; }
}

/// <summary>Represents one post's binding to an entity.</summary>
public sealed record EntityRelationModel
{
    /// <summary>Gets the post.</summary>
    public required Guid PostId { get; init; }

    /// <summary>Gets its role.</summary>
    public required string Role { get; init; }

    /// <summary>Gets what an update changed.</summary>
    public IReadOnlyList<string> Changes { get; init; } = [];

    /// <summary>Gets the occurrence it is about.</summary>
    public Guid? OccurrenceId { get; init; }

    /// <summary>Gets the source that published the post, by handle.</summary>
    public string? Source { get; init; }

    /// <summary>Gets the match confidence, 0 to 1.</summary>
    public decimal Confidence { get; init; }

    /// <summary>Gets a listing post's own offer: author kind, price, link, source and first seen.</summary>
    public JsonElement? Offer { get; init; }

    /// <summary>Gets when the binding was first made.</summary>
    public required DateTimeOffset CreatedAtUtc { get; init; }
}

/// <summary>Represents an entity's occurrences of one year.</summary>
public sealed record EntityYearModel
{
    /// <summary>Gets the year.</summary>
    public required int Year { get; init; }

    /// <summary>Gets the occurrences starting that year, earliest first.</summary>
    public required IReadOnlyList<EntityOccurrenceModel> Occurrences { get; init; }
}

/// <summary>Represents an entity with its occurrences by year and every relation.</summary>
public sealed record EntityDetailModel
{
    /// <summary>Gets the entity.</summary>
    public required EntityModel Entity { get; init; }

    /// <summary>Gets the entity asked for when it was merged into <see cref="Entity"/>.</summary>
    public Guid? RequestedId { get; init; }

    /// <summary>Gets the occurrences by year, earliest first.</summary>
    public required IReadOnlyList<EntityYearModel> Years { get; init; }

    /// <summary>Gets every relation, earliest first.</summary>
    public required IReadOnlyList<EntityRelationModel> Relations { get; init; }
}

/// <summary>Represents one search hit.</summary>
public sealed record EntitySearchHitModel
{
    /// <summary>Gets the entity's id.</summary>
    public required Guid Id { get; init; }

    /// <summary>Gets its container.</summary>
    public required Guid ContainerId { get; init; }

    /// <summary>Gets its type.</summary>
    public required string EntityType { get; init; }

    /// <summary>Gets its canonical name.</summary>
    public required string Name { get; init; }

    /// <summary>Gets its primary taxonomy node.</summary>
    public string? PrimaryDomain { get; init; }

    /// <summary>Gets its cadence.</summary>
    public required string Cadence { get; init; }

    /// <summary>Gets its status.</summary>
    public required string Status { get; init; }

    /// <summary>Gets how similar its name or an alias is to the query, 0 to 1.</summary>
    public required decimal Score { get; init; }

    /// <summary>Gets the years its real occurrences start in, earliest first.</summary>
    public required IReadOnlyList<int> Years { get; init; }

    /// <summary>Gets the predicted next start, when the cadence predicts one.</summary>
    public DateOnly? Predicted { get; init; }

    /// <summary>Gets how many posts bind to it.</summary>
    public long Posts { get; init; }
}

/// <summary>Represents a finished merge.</summary>
public sealed record EntityMergeModel
{
    /// <summary>Gets the entity merged away.</summary>
    public required Guid FromId { get; init; }

    /// <summary>Gets the entity that stays.</summary>
    public required Guid IntoId { get; init; }

    /// <summary>Gets how many relations moved.</summary>
    public int Relations { get; init; }

    /// <summary>Gets how many occurrences moved; an occurrence of the same edition folds into the one that stays.</summary>
    public int Occurrences { get; init; }
}

/// <summary>Represents a keep-apart pair.</summary>
public sealed record EntityKeepApartModel
{
    /// <summary>Gets the lower entity id.</summary>
    public required Guid LeftId { get; init; }

    /// <summary>Gets the higher entity id.</summary>
    public required Guid RightId { get; init; }

    /// <summary>Gets whether this call recorded the pair; false when it was already kept apart.</summary>
    public bool Created { get; init; }
}

/// <summary>Represents the entity layer's counts.</summary>
public sealed record EntityReportModel
{
    /// <summary>Gets entities not merged away, by type.</summary>
    public required IReadOnlyDictionary<string, long> Types { get; init; }

    /// <summary>Gets events by cadence.</summary>
    public required IReadOnlyDictionary<string, long> Cadences { get; init; }

    /// <summary>Gets entities by status.</summary>
    public required IReadOnlyDictionary<string, long> Statuses { get; init; }

    /// <summary>Gets occurrences by status, predicted ones apart as <c>predicted</c>.</summary>
    public required IReadOnlyDictionary<string, long> Occurrences { get; init; }

    /// <summary>Gets relations by role.</summary>
    public required IReadOnlyDictionary<string, long> Roles { get; init; }

    /// <summary>Gets open unsure flags by field and reason, most frequent first.</summary>
    public required IReadOnlyList<EntityFlagCount> Flags { get; init; }

    /// <summary>Gets the entities merged away.</summary>
    public long Merged { get; init; }

    /// <summary>Gets the keep-apart pairs.</summary>
    public long KeptApart { get; init; }

    /// <summary>Gets the posts waiting in the queue.</summary>
    public long Queued { get; init; }
}

/// <summary>Represents how many open flags share a field and reason.</summary>
public sealed record EntityFlagCount
{
    /// <summary>Gets the field: <c>match</c> or <c>name</c>.</summary>
    public required string Field { get; init; }

    /// <summary>Gets the reason: <c>weak-match</c>, <c>ambiguous</c> or <c>no-name</c>.</summary>
    public required string Reason { get; init; }

    /// <summary>Gets how many.</summary>
    public required long Count { get; init; }
}
