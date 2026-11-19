using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;

/// <summary>Represents a post as resolution reads it: where it lives, its current content hash, when it was seen, the
/// link that supplies its title and its author's key on that link's source.</summary>
internal sealed record EntityPost(
    Guid PostId,
    Guid ContainerId,
    byte[] ContentHash,
    bool Current,
    DateTimeOffset FirstSeenAt,
    DateTimeOffset LastChangedAt,
    DateOnly? PostedOn,
    Guid? SourceId,
    string? Source,
    string? ExternalKey,
    string? Url,
    string? AuthorKey);

/// <summary>Represents one done extraction answer resolution reads.</summary>
internal sealed record EntityAnswer(string UseCase, string Role, string Json, DateTimeOffset UpdatedAt);

/// <summary>Represents a post's existing binding for one use case.</summary>
internal sealed record EntityBinding(Guid EntityId, Guid? OccurrenceId, byte[] ContentHash);

/// <summary>Represents an entity row while resolution or a merge changes it.</summary>
internal sealed class EntityRow
{
    internal required Guid Id { get; init; }
    internal required Guid ContainerId { get; init; }
    internal required string EntityType { get; init; }
    internal required string Name { get; set; }
    internal required string NameKey { get; set; }
    internal List<string> Aliases { get; set; } = [];
    internal List<string> AliasKeys { get; set; } = [];
    internal string? PrimaryDomain { get; set; }
    internal JsonObject Facts { get; set; } = new();
    internal string Status { get; set; } = "active";
    internal string Cadence { get; set; } = "unknown";
    internal decimal CadenceConfidence { get; set; }
    internal required DateTimeOffset FirstSeenAt { get; set; }
    internal required DateTimeOffset LastSeenAt { get; set; }
    internal Guid? MergedInto { get; set; }
    internal bool IsNew { get; init; }

    /// <summary>Adds a name to the aliases unless it is the canonical name or already there; twenty at most.</summary>
    internal void AddAlias(string? alias)
    {
        string? name = EntityJson.Clip(alias?.Trim(), 300);
        if (string.IsNullOrEmpty(name) || string.Equals(name, Name, StringComparison.OrdinalIgnoreCase)
            || Aliases.Contains(name, StringComparer.OrdinalIgnoreCase) || Aliases.Count >= 20)
            return;
        Aliases.Add(name);
        string key = EntityNames.Key(name);
        if (key.Length > 0 && key != NameKey && !AliasKeys.Contains(key))
            AliasKeys.Add(key);
    }

    /// <summary>Moves the seen window to include a post's first sighting and last change.</summary>
    internal void Seen(EntityPost post)
    {
        if (post.FirstSeenAt < FirstSeenAt)
            FirstSeenAt = post.FirstSeenAt;
        if (post.LastChangedAt > LastSeenAt)
            LastSeenAt = post.LastChangedAt;
    }
}

/// <summary>Represents an occurrence row while resolution or a merge changes it.</summary>
internal sealed class OccurrenceRow
{
    internal required Guid Id { get; init; }
    internal required Guid EntityId { get; set; }
    internal required string Label { get; set; }
    internal required DateOnly Starts { get; set; }
    internal DateOnly? Ends { get; set; }
    internal required string Precision { get; set; }
    internal required string Certainty { get; set; }
    internal bool Predicted { get; set; }
    internal string Status { get; set; } = "scheduled";
    internal JsonObject? Place { get; set; }
    internal JsonArray History { get; set; } = new();
    internal bool IsNew { get; init; }
    internal bool Dirty { get; set; }
    internal bool Deleted { get; set; }

    /// <summary>Gets the last day the occurrence covers: its end, else the end of its start's year or month at that
    /// precision, else its start.</summary>
    internal DateOnly Last => Ends ?? EntityDates.Cover(Starts, Precision);

    /// <summary>Records a change in the history with the post that made it, then marks the row for writing.</summary>
    internal void Record(string field, JsonNode? from, JsonNode? to, Guid postId, DateTimeOffset at)
    {
        History.Add(new JsonObject
        {
            ["field"] = field, ["from"] = from?.DeepClone(), ["to"] = to?.DeepClone(), ["postId"] = postId.ToString("D"),
            ["at"] = at.ToUniversalTime().ToString("O", CultureInfo.InvariantCulture),
        });
        Dirty = true;
    }

    /// <summary>Returns the occurrence's dates as one JSON value for its history.</summary>
    internal JsonObject Dates() => new()
    {
        ["starts"] = EntityDates.Format(Starts), ["ends"] = Ends is { } ends ? EntityDates.Format(ends) : null,
        ["precision"] = Precision, ["certainty"] = Certainty,
    };
}

/// <summary>Represents one run an answer states: its first and last day, precision and certainty.</summary>
internal sealed record EventSpan(DateOnly Starts, DateOnly? Ends, string Precision, string Certainty)
{
    /// <summary>Gets the last day the span covers.</summary>
    internal DateOnly Last => Ends ?? EntityDates.Cover(Starts, Precision);

    /// <summary>Gets whether a source stated the dates.</summary>
    internal bool Stated => Certainty == "stated";
}

/// <summary>Reads and writes the dates entity rows keep.</summary>
internal static class EntityDates
{
    /// <summary>Returns the last day a date at a precision covers: the year's or month's last day, else the day.</summary>
    internal static DateOnly Cover(DateOnly starts, string precision) => precision switch
    {
        "year" => new DateOnly(starts.Year, 12, 31),
        "month" => new DateOnly(starts.Year, starts.Month, DateTime.DaysInMonth(starts.Year, starts.Month)),
        _ => starts,
    };

    /// <summary>Reads an ISO date at a precision into its first day, or null.</summary>
    internal static DateOnly? Parse(string? value, string precision)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        string text = value.Trim();
        string[] formats = precision switch
        {
            "year" => ["yyyy"],
            "month" => ["yyyy-MM"],
            _ => ["yyyy-MM-dd", "yyyy-MM-dd'T'HH:mm", "yyyy-MM-dd'T'HH:mm:ss"],
        };
        return DateTime.TryParseExact(text, formats, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
            ? DateOnly.FromDateTime(parsed) : null;
    }

    /// <summary>Formats a date as <c>yyyy-MM-dd</c>.</summary>
    internal static string Format(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Whether two spans overlap once the first is widened by <paramref name="windowDays"/> on each side.</summary>
    internal static bool Overlap(DateOnly starts, DateOnly last, DateOnly otherStarts, DateOnly otherLast, int windowDays) =>
        starts.AddDays(-windowDays) <= otherLast && otherStarts <= last.AddDays(windowDays);
}

/// <summary>Holds the JSON helpers entity rows share.</summary>
internal static class EntityJson
{
    /// <summary>Gets the web options rows are written with.</summary>
    internal static JsonSerializerOptions Options { get; } = new(JsonSerializerDefaults.Web);

    /// <summary>Parses a jsonb object column, or returns an empty object.</summary>
    internal static JsonObject Object(string? json) =>
        string.IsNullOrWhiteSpace(json) ? new JsonObject() : JsonNode.Parse(json) as JsonObject ?? new JsonObject();

    /// <summary>Parses a jsonb array column, or returns an empty array.</summary>
    internal static JsonArray Array(string? json) =>
        string.IsNullOrWhiteSpace(json) ? new JsonArray() : JsonNode.Parse(json) as JsonArray ?? new JsonArray();

    /// <summary>Reads a string at a path of object keys, or null.</summary>
    internal static string? Text(JsonNode? node, params string[] path)
    {
        foreach (var key in path)
            node = node is JsonObject json && json.TryGetPropertyValue(key, out var next) ? next : null;
        return node is JsonValue value && value.TryGetValue<string>(out var text) && !string.IsNullOrWhiteSpace(text) ? text : null;
    }

    /// <summary>Reads a number at a path of object keys, or null.</summary>
    internal static decimal? Number(JsonNode? node, params string[] path)
    {
        foreach (var key in path)
            node = node is JsonObject json && json.TryGetPropertyValue(key, out var next) ? next : null;
        return node is JsonValue value && value.TryGetValue<decimal>(out var number) ? number : null;
    }

    /// <summary>Whether two JSON values hold the same data.</summary>
    internal static bool Same(JsonNode? left, JsonNode? right) => JsonNode.DeepEquals(left, right);

    /// <summary>Turns a value into a JSON node with the web options.</summary>
    internal static JsonNode? Node<T>(T value) => JsonSerializer.SerializeToNode(value, Options);

    /// <summary>Cuts a text to <paramref name="max"/> characters without splitting a surrogate pair.</summary>
    internal static string? Clip(string? value, int max) =>
        string.IsNullOrEmpty(value) ? value : value.Length <= max ? value : value[..(char.IsHighSurrogate(value[max - 1]) ? max - 1 : max)];

    /// <summary>Copies each key of <paramref name="source"/> the target lacks or holds as null; never overwrites a value.</summary>
    internal static void FillMissing(JsonObject target, JsonObject source)
    {
        foreach (var (key, value) in source)
        {
            if (value is null)
                continue;
            if (!target.TryGetPropertyValue(key, out var existing) || existing is null)
                target[key] = value.DeepClone();
            else if (existing is JsonObject inner && value is JsonObject incoming)
                FillMissing(inner, incoming);
            else if (existing is JsonArray { Count: 0 } && value is JsonArray { Count: > 0 })
                target[key] = value.DeepClone();
        }
    }
}
