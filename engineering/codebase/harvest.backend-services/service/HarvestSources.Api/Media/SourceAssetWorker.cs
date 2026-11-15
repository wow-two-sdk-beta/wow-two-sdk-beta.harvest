using AngleSharp.Html.Parser;
using WoW.Two.Sdk.Beta.Be.Files.Store;
using WoW.Two.Sdk.Beta.Be.Files.Uploads;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace HarvestSources.Api.Media;

/// <summary>Copies eligible site assets through Harvest's guarded fetcher and the Files upload API.</summary>
internal sealed class SourceAssetWorker(
    SourceAssetStore store,
    IPageFetcher fetcher,
    IFileUploadsBroker uploads,
    TimeProvider clock,
    ILogger<SourceAssetWorker> logger) : BackgroundService
{
    private static readonly TimeSpan ScanInterval = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        DateTimeOffset scanAt = DateTimeOffset.MinValue;
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                if (clock.GetUtcNow() >= scanAt)
                {
                    await store.QueueMissingAsync(stoppingToken);
                    scanAt = clock.GetUtcNow() + ScanInterval;
                }
                if (await store.ClaimAsync(stoppingToken) is { } asset)
                    await CopyAsync(asset, stoppingToken);
                else
                    await Task.Delay(TimeSpan.FromSeconds(5), clock, stoppingToken);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogWarning(error, "The source media worker will retry");
                await Task.Delay(TimeSpan.FromSeconds(15), clock, stoppingToken);
            }
        }
    }

    private async Task CopyAsync(SourceAsset asset, CancellationToken ct)
    {
        var policy = new AssetDestination(asset.Host);
        if (!Uri.TryCreate(asset.Url, UriKind.Absolute, out Uri? origin) || !policy.Allows(origin))
        {
            await store.FinishAsync(asset.Id, null, TimeSpan.FromDays(7), ct);
            return;
        }
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);
        deadline.CancelAfter(TimeSpan.FromMinutes(2));
        var result = asset.Favicon
            ? await FaviconAsync(origin, policy, deadline.Token)
            : await ReadAsync(origin, policy, deadline.Token);
        if (result.Outcome != FetchOutcome.Ok || result.Document is not { } document
            || SourceImageFormat.From(document.ContentType) is not { } format
            || document.Body.IsEmpty || document.Body.Length > (asset.Favicon ? 512 * 1024 : 5 * 1024 * 1024))
        {
            var wait = result.Outcome is FetchOutcome.Ok or FetchOutcome.Gone or FetchOutcome.RobotsDisallowed
                ? TimeSpan.FromDays(1) : result.RetryAfter ?? TimeSpan.FromHours(1);
            await store.FinishAsync(asset.Id, null, wait, ct);
            return;
        }

        using var bytes = new MemoryStream(document.Body.ToArray(), writable: false);
        var saved = await uploads.UploadAsync(bytes, new UploadSpec
        {
            Name = (asset.Favicon ? "favicon" : "post-image") + format.Extension,
            ContentType = format.ContentType,
            Visibility = FileVisibility.Private,
            IdempotencyKey = asset.Id.ToString("D"),
        }, deadline.Token);
        if (saved is Result<FileModel>.Success success)
            await store.FinishAsync(asset.Id, success.Value.Id.Value, TimeSpan.Zero, ct);
        else if (saved is Result<FileModel>.Failure failure)
            logger.LogWarning("Files declined source asset {AssetId}: {Reason}", asset.Id, failure.Error.Message);
    }

    private async Task<FetchResponse> FaviconAsync(Uri home, AssetDestination policy, CancellationToken ct)
    {
        var page = await ReadAsync(home, policy, ct);
        if (page.Outcome is not (FetchOutcome.Ok or FetchOutcome.Gone))
            return page;
        var candidates = new List<Uri>();
        if (page.Document is { } document && document.ContentType?.Contains("html", StringComparison.OrdinalIgnoreCase) == true)
        {
            using var bytes = new MemoryStream(document.Body.ToArray(), writable: false);
            var html = await new HtmlParser().ParseDocumentAsync(bytes, ct);
            foreach (var link in html.QuerySelectorAll("link[rel~='icon'],link[rel='apple-touch-icon']"))
            {
                if (string.IsNullOrWhiteSpace(link.GetAttribute("href")) || link.GetAttribute("type") == "image/svg+xml"
                    || !Uri.TryCreate(document.FinalUrl, link.GetAttribute("href"), out Uri? icon) || !policy.Allows(icon)
                    || icon.AbsolutePath.EndsWith(".svg", StringComparison.OrdinalIgnoreCase))
                    continue;
                candidates.Add(icon);
            }
        }
        candidates.Insert(Math.Min(2, candidates.Count), new Uri(home, "/favicon.ico"));
        foreach (Uri candidate in candidates.Distinct().Take(3))
        {
            var result = await ReadAsync(candidate, policy, ct);
            if ((result.Outcome == FetchOutcome.Ok && SourceImageFormat.From(result.Document?.ContentType) is not null)
                || result.Outcome is not (FetchOutcome.Ok or FetchOutcome.Gone))
                return result;
        }
        return new FetchResponse { Outcome = FetchOutcome.Gone };
    }

    private Task<FetchResponse> ReadAsync(Uri url, AssetDestination policy, CancellationToken ct) =>
        fetcher.FetchAsync(new FetchRequest { Uri = url, DestinationPolicy = policy, CacheResponse = false }, ct);

    private sealed class AssetDestination(string host) : IFetchDestinationPolicy
    {
        public bool Allows(Uri destination) => destination.Scheme == Uri.UriSchemeHttps
            && destination.IsDefaultPort && destination.UserInfo.Length == 0
            && string.Equals(destination.IdnHost, host, StringComparison.OrdinalIgnoreCase);
    }
}

internal sealed record SourceImageFormat(string ContentType, string Extension)
{
    public static SourceImageFormat? From(string? mime) => mime?.ToLowerInvariant() switch
    {
        "image/png" => new("image/png", ".png"),
        "image/jpeg" => new("image/jpeg", ".jpg"),
        "image/webp" => new("image/webp", ".webp"),
        "image/gif" => new("image/gif", ".gif"),
        "image/avif" => new("image/avif", ".avif"),
        "image/x-icon" or "image/vnd.microsoft.icon" => new("image/vnd.microsoft.icon", ".ico"),
        _ => null,
    };
}
