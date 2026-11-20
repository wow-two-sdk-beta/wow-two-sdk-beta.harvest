using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using Capture = WoW.Two.Sdk.Beta.Be.Harvest.Readers.Capture;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Telegram;

/// <summary>Reads one page of a public Telegram channel preview, <c>t.me/s/{username}</c>, into one capture per message.</summary>
/// <remarks>The page's time is the publication time, never an event date. View counts, reactions and the clock label never
/// reach the content projection, so only an edit makes a revision. The reader fetches nothing and runs no script; the
/// older page is pointed to as a listing and the engine's page budget decides whether it is read.</remarks>
public sealed partial class TelegramPreviewCaptureReader : ICaptureReader, IFetchDestinationPolicy
{
    private const int MaxMessages = 100;
    private const int MaxMedia = 20;
    private const int MaxTitle = 240;

    /// <inheritdoc />
    public string Key => TelegramSourceAdapter.AdapterKey;

    /// <inheritdoc />
    public bool Allows(Uri destination) => TelegramChannels.TryReadPreview(destination, out _, out _);

    /// <inheritdoc />
    /// <remarks>Any answer at a preview address is this reader's, so a non-HTML answer fails here rather than reaching a
    /// generic reader.</remarks>
    public bool CanRead(FetchedDocument document) => Allows(document.FinalUrl);

    /// <inheritdoc />
    public async Task<Result<CaptureReadResult>> ReadAsync(FetchedDocument document, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(document);
        cancellationToken.ThrowIfCancellationRequested();
        if (!TelegramChannels.TryReadPreview(document.FinalUrl, out string username, out long? before))
            return Failure("The document is not a public Telegram channel preview page.");
        if (document.ContentType is { Length: > 0 } type && !type.Contains("html", StringComparison.OrdinalIgnoreCase))
            return Failure("The Telegram preview answered something other than HTML.");

        IDocument html = await new HtmlParser().ParseDocumentAsync(Encoding.UTF8.GetString(document.Body.Span), cancellationToken);
        if (html.QuerySelector(".tgme_channel_history") is null)
            return Failure("The page has no Telegram message history: the channel's preview is off, or the page is not a channel.");

        var captures = new List<Capture>();
        var seen = new HashSet<long>();
        long smallest = long.MaxValue;
        foreach (IElement message in html.QuerySelectorAll(".tgme_widget_message[data-post]").Take(MaxMessages))
        {
            if (!TryReadPost(message.GetAttribute("data-post"), username, out long id))
                continue;
            smallest = Math.Min(smallest, id);
            if (seen.Add(id))
                captures.Add(Read(message, username, id, document));
        }

        // An empty page, the first message, or a page that does not lie before the one asked for ends the paging.
        bool older = smallest != long.MaxValue && smallest > 1 && (before is null || smallest < before);
        return Result<CaptureReadResult>.Ok(new()
        {
            Captures = captures,
            Discovered = older ? [new() { Url = TelegramChannels.PreviewUrl(username, smallest), IsListing = true }] : [],
            Complete = false,
        });
    }

    private static Capture Read(IElement message, string username, long id, FetchedDocument document)
    {
        string permalink = TelegramChannels.Permalink(username, id);
        var links = new List<CaptureLink>();
        IElement? body = message.QuerySelector(".tgme_widget_message_text.js-message_text");
        string text = body is null ? string.Empty : TelegramMessageText.Read(body, document.FinalUrl, links);
        string author = Collapse(message.QuerySelector(".tgme_widget_message_owner_name")?.TextContent);
        string forwardedFrom = Collapse(message.QuerySelector(".tgme_widget_message_forwarded_from_name")?.TextContent);
        bool edited = message.QuerySelector(".tgme_widget_message_meta")?.ChildNodes.OfType<IText>()
            .Any(run => run.Data.Contains("edited", StringComparison.OrdinalIgnoreCase)) == true;
        string? stamp = message.QuerySelector(".tgme_widget_message_date time[datetime]")?.GetAttribute("datetime");
        DateTimeOffset? published = DateTimeOffset.TryParse(stamp, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var parsed)
            ? parsed.ToUniversalTime() : null;
        IReadOnlyList<CaptureMedia> media = Media(message);

        string title = text.Split('\n', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault()?.Trim() ?? $"@{username} · {id}";
        if (title.Length > MaxTitle)
            title = title[..MaxTitle];
        string by = author.Length > 0 ? author : "@" + username;
        // The projection holds what the post says, not what its readers did: no counters, reactions, clock label or CDN address.
        string projection = JsonSerializer.Serialize(new
        {
            channel = username, id, text, links = links.Select(link => link.Url), media = media.Select(item => item.Kind.ToString()), edited,
        });
        var evidence = new
        {
            recordKind = "post", channel = username, messageId = id, edited, forwardedFrom = forwardedFrom.Length > 0 ? forwardedFrom : null,
            eventDateStatus = "not-extracted-from-text", coverage = "bounded-recent-messages",
        };
        JsonElement structured = JsonSerializer.SerializeToElement(new { reader = "page", title, text, author = by, telegram = evidence });
        return new()
        {
            ExternalKey = $"{username}/{id}", Url = permalink, OriginKey = permalink,
            RequestUrl = document.RequestUrl.AbsoluteUri, FinalUrl = document.FinalUrl.AbsoluteUri,
            HttpStatus = document.StatusCode, CapturedAtUtc = document.FetchedAtUtc, ContentType = "text/html",
            Body = Encoding.UTF8.GetBytes(message.OuterHtml), ContentProjection = projection,
            PublishedAtUtc = published, Structured = [structured], Media = media, Links = links,
            Warnings = published is null ? ["message_time_unknown"] : [],
        };
    }

    /// <summary>Keeps the message's own photos and videos by reference: a photo's image, a video's address and its thumbnail.</summary>
    /// <remarks>A link preview's image, a reply's thumbnail and a sticker are not the message's media. Nothing is copied.</remarks>
    private static IReadOnlyList<CaptureMedia> Media(IElement message)
    {
        var media = new List<CaptureMedia>();
        void Add(string? address, MediaKind kind)
        {
            if (media.Count < MaxMedia && PublicMedia(address) is { } url && !media.Any(item => item.Url == url))
                media.Add(new() { Url = url, Kind = kind, Position = media.Count });
        }

        foreach (IElement photo in message.QuerySelectorAll("a.tgme_widget_message_photo_wrap"))
            Add(BackgroundImage(photo.GetAttribute("style")), MediaKind.Image);
        foreach (IElement player in message.QuerySelectorAll("a.tgme_widget_message_video_player, a.tgme_widget_message_roundvideo_player"))
        {
            foreach (IElement video in player.QuerySelectorAll("video[src]"))
                Add(video.GetAttribute("src"), MediaKind.Video);
            Add(BackgroundImage(player.QuerySelector(".tgme_widget_message_video_thumb, .tgme_widget_message_roundvideo_thumb")?.GetAttribute("style")),
                MediaKind.Thumbnail);
        }
        return media;
    }

    private static string? BackgroundImage(string? style) => style is not null && BackgroundUrl().Match(style) is { Success: true } match
        ? match.Groups["url"].Value : null;

    private static string? PublicMedia(string? address)
    {
        if (!Uri.TryCreate(address?.Trim(), UriKind.Absolute, out Uri? url) || url.Scheme != Uri.UriSchemeHttps || !url.IsDefaultPort
            || url.UserInfo.Length > 0 || url.Fragment.Length > 0)
            return null;
        string host = url.IdnHost.ToLowerInvariant();
        return host is "telesco.pe" or "cdn-telegram.org" or "telegram.org" || host.EndsWith(".telesco.pe", StringComparison.Ordinal)
            || host.EndsWith(".cdn-telegram.org", StringComparison.Ordinal) ? url.AbsoluteUri : null;
    }

    private static bool TryReadPost(string? post, string username, out long id)
    {
        id = 0;
        return post is not null && DataPost().Match(post) is { Success: true } match
            && match.Groups["name"].Value.Equals(username, StringComparison.OrdinalIgnoreCase)
            && long.TryParse(match.Groups["id"].Value, out id) && id > 0;
    }

    private static string Collapse(string? value) => Spaces().Replace(value ?? string.Empty, " ").Trim();

    private static Result<CaptureReadResult> Failure(string message) => Result<CaptureReadResult>.Fail(AppErrorFactory.Validation(message));

    [GeneratedRegex("^(?<name>[A-Za-z][A-Za-z0-9_]{3,31})/(?<id>[0-9]{1,12})$", RegexOptions.CultureInvariant)]
    private static partial Regex DataPost();

    [GeneratedRegex("background-image\\s*:\\s*url\\(\\s*['\"]?(?<url>[^'\")]+)['\"]?\\s*\\)", RegexOptions.CultureInvariant | RegexOptions.IgnoreCase)]
    private static partial Regex BackgroundUrl();

    [GeneratedRegex("\\s+", RegexOptions.CultureInvariant)]
    private static partial Regex Spaces();
}
