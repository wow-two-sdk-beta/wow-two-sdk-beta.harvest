using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

/// <summary>Turns a post into what the pass hashes and what its prompt receives.</summary>
internal static class PostTagsContent
{
    private const char Separator = '\u001f';
    private const int MaxDates = 10;
    private const int MaxPlaces = 5;

    /// <summary>Gets the SQL twin of <see cref="Hash(string?, string?)"/> over a <c>harvest_posts.base</c> expression.</summary>
    internal static string SqlHash(string baseColumn) =>
        $"sha256(convert_to(COALESCE({baseColumn}->>'title','') || chr(31) || COALESCE({baseColumn}->>'text',''),'UTF8'))";

    /// <summary>Hashes the fields tagging reads: title and text, nothing else, so a revision that only writes tags back
    /// keeps the hash and never queues tagging again.</summary>
    internal static byte[] Hash(string? title, string? text) =>
        SHA256.HashData(Encoding.UTF8.GetBytes($"{title}{Separator}{text}"));

    /// <summary>Hashes an envelope's title and text.</summary>
    internal static byte[] Hash(PostEnvelope envelope) => Hash(envelope.Title, envelope.Text);

    /// <summary>Whether the post has anything to tag.</summary>
    internal static bool HasContent(PostEnvelope envelope) =>
        !string.IsNullOrWhiteSpace(envelope.Title) || !string.IsNullOrWhiteSpace(envelope.Text);

    /// <summary>Cuts <paramref name="text"/> to <paramref name="max"/> characters without splitting a surrogate pair.</summary>
    internal static string Cut(string? text, int max)
    {
        if (string.IsNullOrEmpty(text) || text.Length <= max)
            return text ?? "";
        int end = char.IsHighSurrogate(text[max - 1]) ? max - 1 : max;
        return text[..end];
    }

    /// <summary>Builds the prompt variables: <c>title</c>, <c>text</c> (cut), <c>sourceCategory</c>, <c>language</c>,
    /// <c>dates</c>, <c>place</c>, <c>postedAt</c> and <c>taxonomy</c>; an absent value reads <c>unknown</c> or
    /// <c>none</c>, never empty.</summary>
    internal static Dictionary<string, string?> Variables(PostEnvelope envelope, string? sourceCategory, string taxonomy, int maxTextChars) => new()
    {
        ["title"] = envelope.Title?.Trim() ?? "",
        ["text"] = Cut(envelope.Text, maxTextChars),
        ["sourceCategory"] = string.IsNullOrWhiteSpace(sourceCategory) ? "unknown" : sourceCategory.Trim(),
        ["language"] = envelope.Language?.Code ?? "und",
        ["dates"] = Dates(envelope.Dates),
        ["place"] = Place(envelope.Location),
        ["postedAt"] = PostedAt(envelope.PostedAt),
        ["taxonomy"] = taxonomy,
    };

    /// <summary>Renders the post's stated dates, one <c>role: value[ – end]</c> per date; a date the extraction pass
    /// estimated stays out, so no prompt reads a model's own estimate as the post's.</summary>
    internal static string Dates(IReadOnlyList<PostDate> dates)
    {
        var stated = dates.Where(date => date.Certainty != DateCertainty.Estimated).Take(MaxDates).ToArray();
        return stated.Length == 0 ? "none"
            : string.Join("; ", stated.Select(date =>
                $"{date.Role.ToString().ToLowerInvariant()}: {date.Value}{(date.End is { Length: > 0 } end ? " – " + end : "")}"));
    }

    /// <summary>Renders where the post is about from its location mentions.</summary>
    internal static string Place(PostLocation? location)
    {
        var mentions = location?.Mentions.Select(mention => mention.Text.Trim()).Where(text => text.Length > 0)
            .Distinct(StringComparer.Ordinal).Take(MaxPlaces).ToArray() ?? [];
        return mentions.Length == 0 ? "unknown" : string.Join(", ", mentions);
    }

    /// <summary>Renders the publication date as <c>yyyy-MM-dd</c>, the publisher's own calendar date, or <c>unknown</c>.</summary>
    internal static string PostedAt(PostTimestamp? postedAt)
    {
        if (postedAt?.Value is not { Length: >= 10 } value)
            return "unknown";
        if (DateOnly.TryParseExact(value[..10], "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
            return date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
        return DateTimeOffset.TryParse(value, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) : "unknown";
    }
}
