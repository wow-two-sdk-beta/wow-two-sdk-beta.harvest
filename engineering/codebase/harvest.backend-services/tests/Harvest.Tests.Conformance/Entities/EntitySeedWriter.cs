using System.Security.Cryptography;
using System.Text.Json.Nodes;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Harvest.Entities;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Entities;

namespace Harvest.Tests.Conformance.Entities;

/// <summary>Writes a seeded entity into a PostgreSQL store through the entity layer's own writers, as resolution leaves
/// it: the entity, its occurrences, its relations and the creation events of its change feed.</summary>
internal static class EntitySeedWriter
{
    internal static async Task WriteAsync(string connectionString, EntitySeed seed)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        var entity = new EntityRow
        {
            Id = seed.Id, ContainerId = seed.ContainerId, EntityType = seed.EntityType, Name = seed.Name, NameKey = EntityNames.Key(seed.Name),
            PrimaryDomain = seed.PrimaryDomain, Status = seed.Status, Cadence = seed.Cadence, CadenceConfidence = seed.Cadence == "unknown" ? 0 : 0.8m,
            FirstSeenAt = seed.SeenAtUtc, LastSeenAt = seed.SeenAtUtc, IsNew = true,
        };
        foreach (var alias in seed.Aliases)
            entity.AddAlias(alias);
        var rows = seed.Occurrences.Select(occurrence => new OccurrenceRow
        {
            Id = occurrence.Id, EntityId = seed.Id, Label = occurrence.Label, Starts = occurrence.Starts, Ends = occurrence.Ends,
            Precision = occurrence.Precision, Certainty = occurrence.Certainty, Predicted = occurrence.Predicted, Status = occurrence.Status,
            Place = occurrence.Place is { } place
                ? new JsonObject { ["venue"] = place.Venue, ["city"] = place.City, ["country"] = place.Country } : null,
            IsNew = true,
        }).ToList();
        await EntityStore.SaveEntityAsync(connection, transaction, entity, seed.SeenAtUtc, CancellationToken.None);
        await EntityStore.SaveOccurrencesAsync(connection, transaction, rows, seed.SeenAtUtc, CancellationToken.None);
        foreach (var relation in seed.Relations)
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO harvest_entity_relations(post_id,entity_id,container_id,content_hash,use_case,occurrence_id,role,changes,confidence,created_at,
                    updated_at)
                VALUES(@post,@entity,@container,@hash,@useCase,@occurrence,@role,@changes,1,@at,@at)
                """, connection, transaction);
            command.Parameters.AddWithValue("post", relation.PostId);
            command.Parameters.AddWithValue("entity", seed.Id);
            command.Parameters.AddWithValue("container", seed.ContainerId);
            command.Parameters.AddWithValue("hash", RandomNumberGenerator.GetBytes(32));
            command.Parameters.AddWithValue("useCase", seed.EntityType == EntityTypes.Event ? "extract-event" : "extract-listing");
            command.Parameters.AddWithValue("occurrence", relation.OccurrenceId is { } id ? id : DBNull.Value);
            command.Parameters.AddWithValue("role", relation.Role);
            command.Parameters.AddWithValue("changes", relation.Changes.ToArray());
            command.Parameters.AddWithValue("at", relation.BoundAtUtc);
            await command.ExecuteNonQueryAsync();
        }
        await EntityEventLog.AppendAsync(connection, transaction, EntityEventLog.Diff(null, entity, rows, null, seed.SeenAtUtc), CancellationToken.None);
        await transaction.CommitAsync();
    }
}
