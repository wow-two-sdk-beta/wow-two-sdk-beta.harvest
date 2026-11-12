using System.Globalization;
using System.Text.Json;
using System.Text.RegularExpressions;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using Capture = WoW.Two.Sdk.Beta.Be.Harvest.Readers.Capture;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;

/// <summary>Projects media records without metrics, paging URLs or credentials. Publication time is never an event date.</summary>
public static partial class InstagramCaptureReader
{
    /// <summary>Reads one media record into a durable capture; malformed identity or off-platform permalinks are rejected.</summary>
    /// <param name="record">The bounded API media record.</param>
    /// <param name="username">The verified requested publisher.</param>
    /// <param name="profileUrl">The public profile being harvested, never the authenticated API request URL.</param>
    /// <param name="capturedAt">The capture clock.</param>
    public static Capture? Read(JsonElement record, string username, string profileUrl, DateTimeOffset capturedAt)
    {
        string? id = InstagramBusinessDiscoveryClient.Text(record, "id");
        string? rawUrl = InstagramBusinessDiscoveryClient.Text(record, "permalink");
        if (id is null || !MediaId().IsMatch(id) || !Uri.TryCreate(rawUrl, UriKind.Absolute, out var uri)
            || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length > 0
            || uri.IdnHost is not ("www.instagram.com" or "instagram.com") || !Permalink().IsMatch(uri.AbsolutePath))
            return null;
        string url = new UriBuilder(uri) { Query = "", Fragment = "" }.Uri.AbsoluteUri;
        string caption = InstagramBusinessDiscoveryClient.Text(record, "caption") ?? "";
        string type = InstagramBusinessDiscoveryClient.Text(record, "media_type") ?? "UNKNOWN";
        string title = caption.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? $"@{username} · {id}";
        if (title.Length > 240) title = title[..240];
        bool announcement = Announcement().IsMatch(caption) && !Retrospective().IsMatch(caption);
        var children = new List<string>();
        var media = new List<CaptureMedia>();
        AddMedia(record, media);
        if (record.TryGetProperty("children", out JsonElement gallery) && gallery.ValueKind == JsonValueKind.Object
            && gallery.TryGetProperty("data", out JsonElement data) && data.ValueKind == JsonValueKind.Array)
            foreach (var child in data.EnumerateArray().Take(20))
            {
                if (InstagramBusinessDiscoveryClient.Text(child, "id") is { } childId && MediaId().IsMatch(childId))
                    children.Add(childId);
                AddMedia(child, media);
            }
        string? publishedText = InstagramBusinessDiscoveryClient.Text(record, "timestamp");
        DateTimeOffset? published = DateTimeOffset.TryParse(publishedText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime() : null;
        // Stable content identity ignores engagement counts and expiring CDN URL signatures.
        string projection = JsonSerializer.Serialize(new { id, username, caption, mediaType = type, children });
        byte[] body = JsonSerializer.SerializeToUtf8Bytes(new { id, username, caption, timestamp = publishedText, permalink = url, media_type = type, children });
        var evidence = new
        {
            recordKind = announcement ? "announcement" : "post", username, mediaId = id, requiresReview = true,
            eventDateStatus = "not-extracted-from-caption", imageTextStatus = "not-read", coverage = "bounded-recent-media",
        };
        JsonElement structured = announcement
            ? JsonSerializer.SerializeToElement(new Dictionary<string, object>
            {
                ["@context"] = "https://schema.org", ["@type"] = "Event", ["name"] = title, ["description"] = caption,
                ["url"] = url, ["organizer"] = new { name = "@" + username, url = profileUrl }, ["instagram"] = evidence,
            })
            : JsonSerializer.SerializeToElement(new { reader = "page", title, text = caption, author = "@" + username, instagram = evidence });
        return new()
        {
            ExternalKey = "instagram:" + id, Url = url, RequestUrl = profileUrl, FinalUrl = profileUrl,
            CapturedAtUtc = capturedAt, ContentType = "application/json",
            Body = body, ContentProjection = projection, PublishedAtUtc = published, OriginKey = url,
            Structured = [structured], Media = media,
            Links = [.. Urls().Matches(caption).Select(match => match.Value.TrimEnd('.', ',', ')', ']'))
                .Where(value => Uri.TryCreate(value, UriKind.Absolute, out var link) && link.Scheme is "http" or "https"
                    && link.UserInfo.Length == 0 && !link.Query.Contains("access_token", StringComparison.OrdinalIgnoreCase))
                .Distinct(StringComparer.Ordinal).Take(20).Select(value => new CaptureLink { Url = value })],
            Warnings = ["Instagram caption classification needs review; publication time is not an event date.", "Image and video text has not been read. CDN media URLs can expire."],
        };
    }

    private static void AddMedia(JsonElement node, List<CaptureMedia> media)
    {
        string? value = InstagramBusinessDiscoveryClient.Text(node, "media_url");
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort || uri.UserInfo.Length > 0
            || !(uri.IdnHost.EndsWith(".cdninstagram.com", StringComparison.OrdinalIgnoreCase) || uri.IdnHost.EndsWith(".fbcdn.net", StringComparison.OrdinalIgnoreCase))
            || uri.Query.Contains("access_token", StringComparison.OrdinalIgnoreCase))
            return;
        media.Add(new() { Url = uri.AbsoluteUri, Kind = InstagramBusinessDiscoveryClient.Text(node, "media_type") == "VIDEO" ? MediaKind.Video : MediaKind.Image, Position = media.Count });
    }

    [GeneratedRegex(@"^[0-9]{1,40}$", RegexOptions.CultureInvariant)]
    private static partial Regex MediaId();
    [GeneratedRegex(@"^/(?:p|reel|tv)/[a-zA-Z0-9_-]+/?$", RegexOptions.CultureInvariant)]
    private static partial Regex Permalink();
    [GeneratedRegex(@"open\s+call|call\s+for|applications?\s+(?:open|invited)|register|registration|tickets?|join\s+us|upcoming|save\s+the\s+date|приглаша|при[её]м\s+заяв|заявки|регистрац|билет|скоро|ariza|ro['‘’ʻ]?yxat|taklif|chipt|қабул|рўйхат", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Announcement();
    [GeneratedRegex(@"highlights|throwback|recap|итоги|как\s+это\s+было|прош[её]л|состоял", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Retrospective();
    [GeneratedRegex(@"https?://[^\s<>""']+", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex Urls();
}
