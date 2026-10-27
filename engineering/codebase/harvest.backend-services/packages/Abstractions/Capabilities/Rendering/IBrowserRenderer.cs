using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Rendering;

/// <summary>Renders pages in a browser, for the render rung; declared now, bound once the render companion exists.</summary>
/// <remarks>Each call carries its own exit and session state and persists nothing.</remarks>
public interface IBrowserRenderer
{
    /// <summary>Renders a page.</summary>
    /// <param name="request">The page and the context to render it in.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The rendered page; <c>OperationTimeout</c> past the request's timeout, <c>ExternalUnavailable</c> when the
    /// browser crashed.</returns>
    Task<Result<RenderResult>> RenderAsync(RenderRequest request, CancellationToken cancellationToken = default);
}
