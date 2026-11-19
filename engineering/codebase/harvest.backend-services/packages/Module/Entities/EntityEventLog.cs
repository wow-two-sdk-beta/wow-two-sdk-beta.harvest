using System.Text.Json;
using System.Text.Json.Nodes;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Entities;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Posts;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;

/// <summary>Represents an entity as it was before a change: what the change feed compares against.</summary>
/// <param name="Status">The entity's status.</param>
/// <param name="Cadence">The event's cadence.</param>
/// <param name="Price">The listing's price spread and the event's prices; compared by value, since jsonb reorders keys.</param>
/// <param name="Occurrences">Each occurrence's state, by id.</param>
internal sealed record EntitySnapshot(string Status, string Cadence, JsonObject Price, IReadOnlyDictionary<Guid, OccurrenceModel> Occurrences);

/// <summary>Represents one row of <c>harvest_entity_events</c> before it is written.</summary>
internal sealed record EntityEventRow(
    Guid EventId,
    string Type,
    Guid ContainerId,
    Guid EntityId,
    string EntityType,
    string? Domain,
    Guid? OccurrenceId,
    Guid? PostId,
    DateTimeOffset At,
    JsonObject Payload);

/// <summary>Turns what resolution and the operator's corrections change into the entity change feed
/// (<c>harvest_entity_events</c>, 015), appended in the changing transaction.</summary>
/// <remarks>
///   - a snapshot taken before a change is compared with the rows after it: a new entity is <c>entity.created</c>; a new
///     real occurrence <c>occurrence.added</c>; a predicted one that turned real <c>occurrence.confirmed</c>; a real one
///     whose dates, status or place moved <c>occurrence.changed</c>; a new predicted one <c>occurrence.predicted</c>,
///     naming the prediction it replaces; a prediction gone with no replacement <c>occurrence.removed</c>; the entity's
///     status, cadence or prices <c>entity.changed</c>
///   - a merge appends <c>entity.merged</c> for the entity merged away, then the one that stays as compared; a split
///     appends <c>entity.split</c> for each entity of the pair
///   - payloads carry names, dates, statuses and places, never post text
/// </remarks>
internal static class EntityEventLog
{
    /// <summary>Takes the state the feed compares a change against.</summary>
    internal static EntitySnapshot Take(EntityRow entity, IEnumerable<OccurrenceRow> rows) =>
        new(entity.Status, entity.Cadence, Price(entity), rows.Where(row => !row.Deleted).ToDictionary(row => row.Id, Model));

    /// <summary>Lists the events a change made, in the order a consumer applies them.</summary>
    /// <param name="before">The state before the change; null for an entity the change created.</param>
    /// <param name="entity">The entity after the change.</param>
    /// <param name="rows">Its occurrences after the change; deleted rows are skipped.</param>
    /// <param name="postId">The post whose resolution made the change.</param>
    /// <param name="at">When.</param>
    internal static List<EntityEventRow> Diff(EntitySnapshot? before, EntityRow entity, IEnumerable<OccurrenceRow> rows, Guid? postId, DateTimeOffset at)
    {
        var events = new List<EntityEventRow>();
        if (before is null)
            events.Add(Event(EntityEventTypes.EntityCreated, entity, postId, at));

        var known = before?.Occurrences ?? new Dictionary<Guid, OccurrenceModel>();
        var live = rows.Where(row => !row.Deleted && row.EntityId == entity.Id).OrderBy(row => row.Starts).ThenBy(row => row.Id).ToList();
        var liveIds = live.Select(row => row.Id).ToHashSet();
        var withdrawn = new Queue<OccurrenceModel>(known.Values.Where(old => old.Predicted && !liveIds.Contains(old.Id))
            .OrderBy(old => old.Starts).ThenBy(old => old.Id));
        foreach (var row in live)
        {
            var now = Model(row);
            if (!known.TryGetValue(row.Id, out var old))
            {
                if (!now.Predicted)
                    events.Add(Event(EntityEventTypes.OccurrenceAdded, entity, postId, at, now));
                else
                    events.Add(Event(EntityEventTypes.OccurrencePredicted, entity, postId, at, now,
                        replaces: withdrawn.TryDequeue(out var replaced) ? replaced.Id : null));
                continue;
            }
            var changes = Changes(old, now);
            if (old.Predicted && !now.Predicted)
                events.Add(Event(EntityEventTypes.OccurrenceConfirmed, entity, postId, at, now, old, changes));
            else if (!now.Predicted && changes.Count > 0)
                events.Add(Event(EntityEventTypes.OccurrenceChanged, entity, postId, at, now, old, changes));
        }
        foreach (var gone in withdrawn.Concat(known.Values.Where(old => !old.Predicted && !liveIds.Contains(old.Id))))
            events.Add(Event(EntityEventTypes.OccurrenceRemoved, entity, postId, at, gone));

        if (before is not null)
        {
            var changed = new List<string>();
            if (before.Status != entity.Status)
                changed.Add("status");
            if (before.Cadence != entity.Cadence)
                changed.Add("cadence");
            if (!JsonNode.DeepEquals(before.Price, Price(entity)))
                changed.Add("price");
            if (changed.Count > 0)
                events.Add(Event(EntityEventTypes.EntityChanged, entity, postId, at, changes: changed));
        }
        return events;
    }

    /// <summary>Builds the event of an entity merged into another.</summary>
    internal static EntityEventRow Merged(EntityRow from, Guid into, DateTimeOffset at) =>
        Event(EntityEventTypes.EntityMerged, from, null, at, related: into);

    /// <summary>Builds the event of an entity an operator keeps apart from another.</summary>
    internal static EntityEventRow Split(EntityRow entity, Guid other, DateTimeOffset at) =>
        Event(EntityEventTypes.EntitySplit, entity, null, at, related: other);

    /// <summary>Appends events inside the changing transaction.</summary>
    internal static async Task AppendAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, IEnumerable<EntityEventRow> events,
        CancellationToken ct)
    {
        foreach (var row in events)
            await PostSql.ExecuteAsync(connection, transaction, """
                INSERT INTO harvest_entity_events(event_id,container_id,entity_id,entity_type,domain,type,occurrence_id,post_id,occurred_at,payload)
                VALUES(@event,@container,@entity,@entityType,@domain,@type,@occurrence,@post,@at,CAST(@payload AS jsonb))
                """, ct, ("event", row.EventId), ("container", row.ContainerId), ("entity", row.EntityId), ("entityType", row.EntityType),
                ("domain", row.Domain), ("type", row.Type), ("occurrence", row.OccurrenceId), ("post", row.PostId), ("at", row.At),
                ("payload", row.Payload.ToJsonString()));
    }

    /// <summary>Returns an occurrence row as the contract shows it.</summary>
    internal static OccurrenceModel Model(OccurrenceRow row) => new()
    {
        Id = row.Id, Label = row.Label, Starts = row.Starts, Ends = row.Ends, Precision = row.Precision, Certainty = row.Certainty,
        Predicted = row.Predicted, Status = row.Status, Place = Place(row.Place),
    };

    /// <summary>Returns a stored place as the contract shows it; null when it names nothing.</summary>
    internal static OccurrencePlaceModel? Place(JsonObject? place)
    {
        if (place is null)
            return null;
        var model = new OccurrencePlaceModel
        {
            Venue = EntityJson.Text(place, "venue"), City = EntityJson.Text(place, "city"), Country = EntityJson.Text(place, "country"),
        };
        return model is { Venue: null, City: null, Country: null } ? null : model;
    }

    /// <summary>Names what moved between two states of one occurrence: <c>dates</c>, <c>status</c>, <c>place</c>.</summary>
    internal static List<string> Changes(OccurrenceModel old, OccurrenceModel now)
    {
        var changes = new List<string>();
        if (old.Starts != now.Starts || old.Ends != now.Ends || old.Precision != now.Precision || old.Certainty != now.Certainty)
            changes.Add("dates");
        if (old.Status != now.Status)
            changes.Add("status");
        if (old.Place != now.Place)
            changes.Add("place");
        return changes;
    }

    private static JsonObject Price(EntityRow entity) => new()
    {
        ["priceSpread"] = entity.Facts["priceSpread"]?.DeepClone(), ["prices"] = entity.Facts["prices"]?.DeepClone(),
    };

    private static EntityEventRow Event(string type, EntityRow entity, Guid? postId, DateTimeOffset at, OccurrenceModel? occurrence = null,
        OccurrenceModel? previous = null, IReadOnlyList<string>? changes = null, Guid? related = null, Guid? replaces = null)
    {
        var payload = new JsonObject { ["name"] = entity.Name, ["status"] = entity.Status, ["cadence"] = entity.Cadence };
        if (occurrence is not null)
            payload["occurrence"] = JsonSerializer.SerializeToNode(occurrence, EntityJson.Options);
        if (previous is not null)
            payload["previous"] = JsonSerializer.SerializeToNode(previous, EntityJson.Options);
        if (changes is { Count: > 0 })
            payload["changes"] = new JsonArray([.. changes.Select(change => (JsonNode?)JsonValue.Create(change))]);
        if (related is { } relatedId)
            payload["relatedEntityId"] = relatedId.ToString("D");
        if (replaces is { } replacedId)
            payload["replacesOccurrenceId"] = replacedId.ToString("D");
        return new EntityEventRow(Guid.CreateVersion7(at), type, entity.ContainerId, entity.Id, entity.EntityType, entity.PrimaryDomain,
            occurrence?.Id, postId, at, payload);
    }
}
