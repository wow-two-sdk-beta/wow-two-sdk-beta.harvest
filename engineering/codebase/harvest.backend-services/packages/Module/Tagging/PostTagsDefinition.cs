using System.Text.Json;
using System.Text.Json.Schema;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

/// <summary>Describes the versioned <see cref="PostTagsConstants.UseCase"/> use case: its code-owned answer schema and the
/// prompt variables the pass supplies.</summary>
/// <remarks>The answer is one object: <c>entityType</c> (<c>event</c> · <c>listing</c> · <c>none</c> · <c>unsure</c>),
/// <c>domains</c> (<c>[{node, primary}]</c>, taxonomy keys), <c>topics</c> (open English tags), <c>about</c>,
/// <c>timeframe</c> (<c>upcoming</c> · <c>ongoing</c> · <c>past</c> · <c>evergreen</c> · <c>unsure</c>), <c>items</c>,
/// <c>language</c>, <c>confidence</c> (0–1), <c>titleEn</c> and <c>flags</c> (<c>[{field, reason, proposed}]</c>).</remarks>
public static class PostTagsDefinition
{
    /// <summary>The user and system template variables the pass supplies, by name.</summary>
    public static IReadOnlyList<string> Variables { get; } =
        ["title", "text", "sourceCategory", "language", "dates", "place", "postedAt", "taxonomy"];

    internal static readonly JsonSerializerOptions Wire = new(JsonSerializerDefaults.Web)
    {
        PropertyNameCaseInsensitive = false,
        RespectRequiredConstructorParameters = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        NumberHandling = JsonNumberHandling.Strict,
        TypeInfoResolver = new DefaultJsonTypeInfoResolver(),
        Converters = { new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false) },
    };

    // Declared after Wire: static initializers run in text order.
    /// <summary>The JSON schema every answer must match, as the use case registry stores it.</summary>
    public static string OutputSchema { get; } = BuildSchema();

    /// <summary>Reads a model answer, or returns null when it does not match the schema's shape.</summary>
    internal static PostTagsAnswer? Parse(string? output)
    {
        if (string.IsNullOrWhiteSpace(output))
            return null;
        try
        {
            return JsonSerializer.Deserialize<PostTagsAnswer>(output, Wire);
        }
        catch (JsonException)
        {
            return null;
        }
    }

    private static string BuildSchema()
    {
        var options = new JsonSchemaExporterOptions { TreatNullObliviousAsNonNullable = true };
        return Wire.GetJsonSchemaAsNode(typeof(PostTagsAnswer), options).AsObject().ToJsonString();
    }
}

/// <summary>Refers to what kind of entity a post is about (scfd-entity-model #4).</summary>
internal enum PostEntityType { Event, Listing, None, Unsure }

/// <summary>Refers to when the post's subject happens relative to its publication.</summary>
internal enum PostTimeframe { Upcoming, Ongoing, Past, Evergreen, Unsure }

/// <summary>Represents one model answer as the schema states it.</summary>
internal sealed record PostTagsAnswer(
    PostEntityType EntityType,
    IReadOnlyList<PostTagsDomain> Domains,
    IReadOnlyList<string> Topics,
    string About,
    PostTimeframe Timeframe,
    int Items,
    string Language,
    double Confidence,
    string TitleEn,
    IReadOnlyList<PostTagsFlag> Flags);

/// <summary>Represents a taxonomy node the answer chose, and whether it is the post's primary domain.</summary>
internal sealed record PostTagsDomain(string Node, bool Primary);

/// <summary>Represents an unsure field: why, and the value the model would add when it fits.</summary>
internal sealed record PostTagsFlag(string Field, string Reason, string? Proposed);
