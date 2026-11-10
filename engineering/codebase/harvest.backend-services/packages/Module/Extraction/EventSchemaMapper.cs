using System.Text.Json;
using System.Text.Json.Serialization;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Extraction;

/// <summary>Maps an extracted event into the deterministic consumer payload without inventing missing fields.</summary>
public static class EventSchemaMapper
{
    /// <summary>Holds the payload's wire options: camelCase names and camelCase string enums, keeping nulls, so an absent
    /// value reads as an explicit null.</summary>
    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web)
    {
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase) },
    };

    /// <summary>Maps an event envelope into <c>wow2.event.v1</c>, or returns null for another post kind.</summary>
    /// <param name="envelope">The deterministic base-envelope reading.</param>
    /// <returns>The event schema, title, description, dates, organizer, place, offers, source structure and the mapping
    /// stamp that names the versions which produced it.</returns>
    public static JsonElement? Map(PostEnvelope envelope)
    {
        if (envelope.Kind != PostKind.Event) return null;
        return JsonSerializer.SerializeToElement(new
        {
            schema = PostEnvelopeConstants.EventSchema,
            title = envelope.Title,
            description = envelope.Text,
            starts = Date(envelope, DateRole.Start),
            ends = Date(envelope, DateRole.End),
            postedAt = envelope.PostedAt,
            organizer = envelope.FieldSources.ContainsKey("organizer") ? envelope.Author : null,
            place = envelope.Location,
            offers = envelope.Prices,
            contacts = envelope.Contacts,
            sourceTags = envelope.SourceTags,
            links = envelope.Links,
            media = envelope.Media,
            language = envelope.Language,
            schemaOrg = envelope.Extras.TryGetValue("schemaOrg", out var original) ? (JsonElement?)original : null,
            mapping = new
            {
                schema = PostEnvelopeConstants.EventSchema,
                norm = envelope.Extraction.NormVersion,
                rules = envelope.Extraction.RulesVersion,
                parser = envelope.FieldSources.TryGetValue("kind", out var kind) ? kind.Rule : null,
            },
        }, Options);
    }

    private static object? Date(PostEnvelope envelope, DateRole role)
    {
        var date = envelope.Dates.FirstOrDefault(value => value.Role == role);
        return date is null ? null : new { value = date.Value, precision = date.Precision.ToString().ToLowerInvariant() };
    }
}
