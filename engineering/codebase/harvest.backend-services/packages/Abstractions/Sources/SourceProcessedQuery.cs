using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Sources;

/// <summary>Filters and pages the posts of one source that the AI passes processed, newest first seen first.</summary>
public sealed record SourceProcessedQuery
{
    /// <summary>The page size when none is asked for.</summary>
    public const int DefaultLimit = 30;

    /// <summary>The largest page.</summary>
    public const int MaxLimit = 90;

    /// <summary>Gets the entity type the tags answer must name: <c>event</c>, <c>listing</c>, <c>none</c> or
    /// <c>unsure</c>; none keeps every type.</summary>
    public string? EntityType { get; init; }

    /// <summary>Gets the relevance verdict the post must carry; none keeps every post, with or without a verdict.</summary>
    public ClassificationVerdict? Verdict { get; init; }

    /// <summary>Gets whether the post must have a done extraction answer (<c>true</c>) or must not (<c>false</c>);
    /// none keeps both.</summary>
    public bool? Extracted { get; init; }

    /// <summary>Gets the page size, 1 to <see cref="MaxLimit"/>.</summary>
    public int Limit { get; init; } = DefaultLimit;

    /// <summary>Gets the cursor a previous page of the same source and filters returned.</summary>
    public string? Cursor { get; init; }
}

/// <summary>A page of a source's processed posts.</summary>
/// <param name="Items">The posts, newest first seen first.</param>
/// <param name="NextCursor">The cursor of the next page, or null at the end.</param>
/// <param name="Total">How many processed posts match the filters, across every page.</param>
public sealed record SourceProcessedPage(IReadOnlyList<SourceProcessedPostModel> Items, string? NextCursor, int Total);

/// <summary>Holds the request rules every form of the processed read applies: the filter values, the page size and the
/// opaque cursor bound to its source and filters.</summary>
public static class SourceProcessedQueryRules
{
    /// <summary>Holds the entity types a tags answer names, the filter's values.</summary>
    public static IReadOnlyList<string> EntityTypes { get; } = ["event", "listing", "none", "unsure"];

    /// <summary>Checks a query's filters, size and cursor for a source.</summary>
    /// <param name="sourceId">The source the query reads.</param>
    /// <param name="query">The query.</param>
    /// <param name="after">The position the cursor continues after: the first-seen time and post id of the last post
    /// shown; null without a cursor.</param>
    /// <returns>The validation error, or null when the query is valid.</returns>
    public static AppError? Check(Guid sourceId, SourceProcessedQuery query, out (DateTimeOffset At, Guid PostId)? after)
    {
        ArgumentNullException.ThrowIfNull(query);
        after = null;
        if (query.Limit is < 1 or > SourceProcessedQuery.MaxLimit)
            return RegistrationErrors.Invalid($"The limit must be between 1 and {SourceProcessedQuery.MaxLimit}.");
        if (query.EntityType is { } type && !EntityTypes.Contains(type, StringComparer.Ordinal))
            return RegistrationErrors.Invalid("The entity type must be event, listing, none or unsure.");
        if (query.Verdict is { } verdict && !Enum.IsDefined(verdict))
            return RegistrationErrors.Invalid("The verdict must be relevant, irrelevant or uncertain.");
        if (query.Cursor is null)
            return null;
        try
        {
            if (query.Cursor.Length <= 1024
                && JsonSerializer.Deserialize<Position>(Convert.FromBase64String(query.Cursor)) is { } position
                && position.Signature == Signature(sourceId, query) && position.PostId != Guid.Empty)
            {
                after = (position.At, position.PostId);
                return null;
            }
        }
        catch (Exception error) when (error is JsonException or FormatException)
        {
        }
        return RegistrationErrors.Invalid("The processed posts cursor is invalid or belongs to another query.");
    }

    /// <summary>Writes the cursor that continues after a post.</summary>
    /// <param name="sourceId">The source the query reads.</param>
    /// <param name="query">The query, whose filters bind the cursor.</param>
    /// <param name="last">The last post of the page.</param>
    /// <returns>The opaque cursor.</returns>
    public static string Cursor(Guid sourceId, SourceProcessedQuery query, SourceProcessedPostModel last)
    {
        ArgumentNullException.ThrowIfNull(last);
        return Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(new Position(Signature(sourceId, query), last.FirstSeenAt, last.PostId)));
    }

    /// <summary>Whether a post sorts after the cursor's position: newest first seen first, then by post id descending.</summary>
    /// <param name="post">The post.</param>
    /// <param name="after">The cursor's position.</param>
    /// <returns>Whether the post belongs to a later page.</returns>
    public static bool After(SourceProcessedPostModel post, (DateTimeOffset At, Guid PostId)? after)
    {
        ArgumentNullException.ThrowIfNull(post);
        return after is not { } position || post.FirstSeenAt < position.At
            || post.FirstSeenAt == position.At && post.PostId.CompareTo(position.PostId) < 0;
    }

    private static string Signature(Guid sourceId, SourceProcessedQuery query) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(
            $"{sourceId:D}|{query.EntityType}|{query.Verdict}|{query.Extracted}")))[..32];

    private sealed record Position(string Signature, DateTimeOffset At, Guid PostId);
}
