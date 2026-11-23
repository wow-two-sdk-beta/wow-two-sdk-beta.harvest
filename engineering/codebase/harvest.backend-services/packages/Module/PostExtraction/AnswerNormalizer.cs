using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization.Metadata;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

/// <summary>Repairs a model answer's JSON before the strict read, so a value the model is unsure of, or one the enum lacks,
/// becomes <c>unsure</c> plus a flag instead of discarding the whole answer (scfd-entity-model #9).</summary>
/// <remarks>
///   - walks the answer along the answer type's own wire shape, nested objects and lists included, and fixes three things
///   - an enum value outside the enum becomes the enum's <c>unsure</c> member, else its <c>unknown</c> member, else <c>null</c>
///     where the field is nullable; with none of these the value stays and the strict read rejects the answer, as before
///   - in a list, an item outside the enum is dropped, and duplicates are removed
///   - matching ignores case and the separators <c>-</c>, <c>_</c> and space, so <c>trade fair</c> reads as <c>trade-fair</c>,
///     silently
///   - an unknown object property is removed
///   - every drop and replacement adds a <c>{field, reason, proposed}</c> flag to the answer's own <c>flags</c> list, which
///     the checks that follow carry on like the model's flags
///   - a listing minimum-stay claim with an invalid count turns that term unknown and adds a flag
///   - other wrong types and missing properties go to the strict read
/// </remarks>
internal sealed class AnswerNormalizer(JsonSerializerOptions wire)
{
    internal const string OutsideList = "value outside the list";
    internal const string UnknownField = "unknown field";
    internal const string InvalidMinimumStay = "the minimum stay has no positive numeric count";
    internal const int MaxProposed = 40;

    private const string FlagsProperty = "flags";

    private readonly ConcurrentDictionary<Type, EnumMap> maps = new();

    /// <summary>Returns the JSON to read as <paramref name="answer"/>: <paramref name="output"/> itself when nothing needed
    /// repair or it is not a JSON object, else the repaired text.</summary>
    /// <param name="output">The model's answer.</param>
    /// <param name="answer">The answer record type, read with the wire options.</param>
    internal string Normalize(string output, Type answer)
    {
        try
        {
            if (JsonNode.Parse(output) is not JsonObject root)
                return output;
            var pass = new Pass();
            if (answer == typeof(ExtractListingAnswer))
                NormalizeMinimumStay(root, pass);
            VisitObject(root, wire.GetTypeInfo(answer), "", pass);
            if (!pass.Changed)
                return output;
            if (pass.Flags.Count > 0)
                AddFlags(root, pass.Flags);
            return root.ToJsonString();
        }
        catch (Exception error) when (error is JsonException or ArgumentException)
        {
            // Not JSON, or a repeated property name (the node tree throws when it first reads one): the strict read decides.
            return output;
        }
    }

    private static void NormalizeMinimumStay(JsonObject root, Pass pass)
    {
        if (root["rentalTerms"] is not JsonObject terms || terms["minimumStay"] is not JsonObject minimum
            || minimum["claims"] is not JsonArray claims)
            return;

        foreach (var claim in claims)
        {
            if (claim is not JsonObject item || item["value"] is not JsonObject duration)
                continue;
            if (duration["count"] is JsonValue value && value.TryGetValue<decimal>(out var count) && count > 0)
                continue;

            minimum["status"] = "unknown";
            minimum["claims"] = new JsonArray();
            pass.Flag("rentalTerms.minimumStay", InvalidMinimumStay, null);
            return;
        }
    }

    private static void AddFlags(JsonObject root, List<JsonObject> flags)
    {
        // Absent or of another type: the strict read rejects the answer for it, so there is nothing to carry.
        if (!root.TryGetPropertyValue(FlagsProperty, out var existing))
            return;
        if (existing is null)
            root[FlagsProperty] = existing = new JsonArray();
        if (existing is JsonArray list)
            foreach (var flag in flags)
                list.Add(flag);
    }

    private void VisitObject(JsonObject node, JsonTypeInfo info, string path, Pass pass)
    {
        foreach (var (name, value) in node.ToArray())
        {
            string field = path.Length == 0 ? name : $"{path}.{name}";
            var property = info.Properties.FirstOrDefault(candidate => string.Equals(candidate.Name, name, StringComparison.Ordinal));
            if (property is null)
            {
                node.Remove(name);
                pass.Flag(field, UnknownField, null);
                continue;
            }
            if (value is not null)
                VisitProperty(node, name, value, property.PropertyType, field, pass);
        }
    }

    private void VisitProperty(JsonObject owner, string name, JsonNode value, Type declared, string field, Pass pass)
    {
        var type = Nullable.GetUnderlyingType(declared) ?? declared;
        if (type.IsEnum)
        {
            if (TextOf(value) is { } text)
                VisitEnum(owner, name, text, type, nullable: type != declared, field, pass);
            return;
        }
        var info = wire.GetTypeInfo(type);
        if (info.Kind == JsonTypeInfoKind.Object && value is JsonObject child)
            VisitObject(child, info, field, pass);
        else if (info.Kind == JsonTypeInfoKind.Enumerable && value is JsonArray list && ElementOf(type) is { } element)
            VisitList(list, element, field, pass);
    }

    private void VisitEnum(JsonObject owner, string name, string text, Type type, bool nullable, string field, Pass pass)
    {
        var map = MapOf(type);
        if (map.Match(text) is { } member)
        {
            if (!string.Equals(member, text, StringComparison.Ordinal))
            {
                owner[name] = member;
                pass.Changed = true;
            }
            return;
        }
        if (map.Fallback is { } fallback)
            owner[name] = fallback;
        else if (nullable)
            owner[name] = null;
        else
            return;
        pass.Flag(field, OutsideList, Proposed(text));
    }

    private void VisitList(JsonArray list, Type declared, string path, Pass pass)
    {
        var type = Nullable.GetUnderlyingType(declared) ?? declared;
        if (type.IsEnum)
        {
            VisitEnumList(list, type, path, pass);
            return;
        }
        var info = wire.GetTypeInfo(type);
        if (info.Kind != JsonTypeInfoKind.Object)
            return;
        for (int index = 0; index < list.Count; index++)
            if (list[index] is JsonObject item)
                VisitObject(item, info, $"{path}[{index}]", pass);
    }

    private void VisitEnumList(JsonArray list, Type type, string path, Pass pass)
    {
        var texts = new List<string>(list.Count);
        foreach (var item in list)
        {
            if (TextOf(item) is not { } text)
                return; // a wrong item type: the strict read rejects the answer
            texts.Add(text);
        }

        var map = MapOf(type);
        var kept = new List<string>();
        var flags = new List<(string Field, string Reason, string? Proposed)>();
        bool changed = false;
        for (int index = 0; index < texts.Count; index++)
        {
            if (map.Match(texts[index]) is not { } member)
            {
                flags.Add(($"{path}[{index}]", OutsideList, Proposed(texts[index])));
                changed = true;
                continue;
            }
            if (kept.Contains(member))
                changed = true;
            else
                kept.Add(member);
            changed |= !string.Equals(member, texts[index], StringComparison.Ordinal);
        }
        if (!changed)
            return;

        list.Clear();
        foreach (var member in kept)
            list.Add(member);
        foreach (var (field, reason, proposed) in flags)
            pass.Flag(field, reason, proposed);
        pass.Changed = true;
    }

    /// <summary>Returns the text of a JSON string node, or null for any other node.</summary>
    private static string? TextOf(JsonNode? node) => node is JsonValue value && value.TryGetValue(out string? text) ? text : null;

    private EnumMap MapOf(Type type) => maps.GetOrAdd(type, key => EnumMap.Build(key, wire));

    private static Type? ElementOf(Type list) =>
        list.IsArray ? list.GetElementType() : list.IsGenericType && list.GetGenericArguments() is [var only] ? only : null;

    /// <summary>Clips a rejected value for its flag; a contact is never kept.</summary>
    private static string? Proposed(string raw)
    {
        string text = raw.Trim();
        if (text.Length == 0 || PostExtractionValidation.HasContact(text))
            return null;
        return text.Length <= MaxProposed ? text : text[..(char.IsHighSurrogate(text[MaxProposed - 1]) ? MaxProposed - 1 : MaxProposed)];
    }

    private sealed class Pass
    {
        internal bool Changed { get; set; }

        internal List<JsonObject> Flags { get; } = [];

        internal void Flag(string field, string reason, string? proposed)
        {
            Changed = true;
            Flags.Add(new JsonObject { ["field"] = field, ["reason"] = reason, ["proposed"] = proposed });
        }
    }

    /// <summary>Holds one enum's wire names by their separator- and case-free key, and the member a stray value becomes.</summary>
    private sealed class EnumMap(Dictionary<string, string> members, string? fallback)
    {
        /// <summary>Gets the member a value outside the enum becomes: <c>unsure</c>, else <c>unknown</c>, else none.</summary>
        internal string? Fallback { get; } = fallback;

        /// <summary>Returns the enum member's wire name that <paramref name="text"/> spells, or null for none.</summary>
        internal string? Match(string text) => members.GetValueOrDefault(Key(text));

        internal static EnumMap Build(Type type, JsonSerializerOptions wire)
        {
            var members = new Dictionary<string, string>();
            string? unsure = null;
            string? unknown = null;
            foreach (var value in Enum.GetValues(type))
            {
                // The converter owns the wire name: naming policy and [JsonStringEnumMemberName] included.
                string name = JsonSerializer.SerializeToElement(value, type, wire).GetString()!;
                members[Key(name)] = name;
                string member = value.ToString()!;
                if (string.Equals(member, "Unsure", StringComparison.OrdinalIgnoreCase))
                    unsure = name;
                else if (string.Equals(member, "Unknown", StringComparison.OrdinalIgnoreCase))
                    unknown = name;
            }
            return new(members, unsure ?? unknown);
        }

        private static string Key(string text) =>
            string.Concat(text.Where(character => character is not ('-' or '_') && !char.IsWhiteSpace(character))).ToLowerInvariant();
    }
}
