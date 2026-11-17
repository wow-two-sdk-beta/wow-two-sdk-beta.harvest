using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;

/// <summary>Reads another eligible professional publisher through Meta's official Business Discovery API.</summary>
public interface IInstagramMediaBroker
{
    /// <summary>Reads a bounded recent-media page; configuration, permission and throttling failures are explicit outcomes.</summary>
    /// <param name="username">The publisher selected by the persisted pipeline scope.</param>
    /// <param name="pageSize">The saved pipeline's requested posts per response.</param>
    /// <param name="after">An opaque cursor from an earlier response.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<InstagramMediaPage> ReadAsync(string username, int pageSize = 25, string? after = null, CancellationToken cancellationToken = default);

    /// <summary>Reads the page with operation details for the worker that requested it.</summary>
    /// <param name="username">The approved publisher.</param>
    /// <param name="pageSize">The saved pipeline's requested posts per response.</param>
    /// <param name="after">The previous page's cursor.</param>
    /// <param name="context">The worker's operation recorder.</param>
    /// <param name="cancellationToken">Cancels the request.</param>
    Task<InstagramMediaPage> ReadTracedAsync(string username, int pageSize, string? after, IStepContext context, CancellationToken cancellationToken)
        => context.RunOperationAsync("fetch", () => ReadAsync(username, pageSize, after, cancellationToken),
            result => new(result.Outcome == FetchOutcome.Ok ? "completed" : "failed", Input: 1,
                Output: result.Media.Count, HttpStatus: result.HttpStatus, Message: result.Outcome.ToString()));
}
