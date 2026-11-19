using System.Text.RegularExpressions;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Entities;

/// <summary>Holds the request rules every form of <see cref="IEntitiesBroker"/> applies, so the module, the client's
/// service and the fake refuse the same requests: filters are trimmed and lowercased, then checked.</summary>
public static partial class EntityQueryRules
{
    /// <summary>Holds the longest search text.</summary>
    public const int MaxQueryLength = 300;

    /// <summary>Holds the earliest year a post filter takes.</summary>
    public const int MinYear = 1900;

    /// <summary>Holds the latest year a post filter takes.</summary>
    public const int MaxYear = 2200;

    /// <summary>Trims and lowercases a filter value; null when blank.</summary>
    /// <param name="value">The value as sent.</param>
    public static string? Normalize(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim().ToLowerInvariant();

    /// <summary>Trims a search text; null when blank.</summary>
    /// <param name="query">The text as sent.</param>
    public static string? Text(string? query) => string.IsNullOrWhiteSpace(query) ? null : query.Trim();

    /// <summary>Whether a taxonomy key is well formed: lowercase slugs joined by <c>/</c>, such as
    /// <c>style-fashion/womens-fashion</c>.</summary>
    /// <param name="domain">The normalized key.</param>
    public static bool IsDomainKey(string domain) => TaxonomyKey().IsMatch(domain);

    /// <summary>Whether <paramref name="node"/> is <paramref name="key"/> or sits under it: <c>style-fashion/womens-fashion</c>
    /// is under <c>style-fashion</c>; <c>style-fashion-x</c> is not. A null key keeps every node.</summary>
    /// <param name="node">An entity's primary domain.</param>
    /// <param name="key">The normalized filter.</param>
    public static bool AtOrUnder(string? node, string? key) =>
        key is null || node is not null && (node == key || node.Length > key.Length && node.StartsWith(key, StringComparison.Ordinal) && node[key.Length] == '/');

    /// <summary>Checks a search; null when it is valid.</summary>
    /// <param name="query">The trimmed text, or null.</param>
    /// <param name="entityType">The normalized type, or null.</param>
    /// <param name="domain">The normalized domain, or null.</param>
    /// <param name="limit">The most entities.</param>
    public static AppError? Search(string? query, string? entityType, string? domain, int limit) =>
        query is { Length: > MaxQueryLength } ? EntityErrors.Invalid($"The query must be 1 to {MaxQueryLength} characters.")
        : limit is < 1 or > EntityRouteConstants.MaxSearchLimit ? EntityErrors.Invalid($"The limit must be between 1 and {EntityRouteConstants.MaxSearchLimit}.")
        : Filters(entityType, domain);

    /// <summary>Checks an entity's posts request; null when it is valid.</summary>
    /// <param name="year">The year, or null.</param>
    /// <param name="role">The normalized role, or null.</param>
    /// <param name="limit">The page size.</param>
    public static AppError? Posts(int? year, string? role, int limit) =>
        year is < MinYear or > MaxYear ? EntityErrors.Invalid($"The year must be between {MinYear} and {MaxYear}.")
        : role is not null && !EntityRoles.All.Contains(role) ? EntityErrors.Invalid($"The role is one of {string.Join(", ", EntityRoles.All)}.")
        : limit is < 1 or > EntityRouteConstants.MaxPostsLimit ? EntityErrors.Invalid($"The limit must be between 1 and {EntityRouteConstants.MaxPostsLimit}.")
        : null;

    /// <summary>Checks a calendar window; null when it is valid.</summary>
    /// <param name="from">The first day.</param>
    /// <param name="to">The last day.</param>
    /// <param name="entityType">The normalized type, or null.</param>
    /// <param name="domain">The normalized domain, or null.</param>
    public static AppError? Calendar(DateOnly from, DateOnly to, string? entityType, string? domain) =>
        to < from ? EntityErrors.Invalid("The window's last day must not come before its first.")
        : to.DayNumber - from.DayNumber > EntityRouteConstants.MaxCalendarDays
            ? EntityErrors.Invalid($"The window spans at most {EntityRouteConstants.MaxCalendarDays} days after its first day.")
        : Filters(entityType, domain);

    /// <summary>Checks a change feed request; null when it is valid.</summary>
    /// <param name="limit">The page size.</param>
    /// <param name="entityType">The normalized type, or null.</param>
    /// <param name="domain">The normalized domain, or null.</param>
    public static AppError? Events(int limit, string? entityType, string? domain) =>
        limit is < 1 or > EntityRouteConstants.MaxEventsLimit ? EntityErrors.Invalid($"The limit must be between 1 and {EntityRouteConstants.MaxEventsLimit}.")
        : Filters(entityType, domain);

    private static AppError? Filters(string? entityType, string? domain) =>
        entityType is not null && !EntityTypes.All.Contains(entityType) ? EntityErrors.Invalid("The type is event or listing.")
        : domain is not null && !IsDomainKey(domain) ? EntityErrors.Invalid("The domain is a taxonomy key such as style-fashion.")
        : null;

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*(/[a-z0-9]+(-[a-z0-9]+)*)*$", RegexOptions.CultureInvariant)]
    private static partial Regex TaxonomyKey();
}
