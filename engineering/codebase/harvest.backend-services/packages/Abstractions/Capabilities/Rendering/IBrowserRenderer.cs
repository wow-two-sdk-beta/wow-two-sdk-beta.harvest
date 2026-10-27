using WoW.Two.Sdk.Beta.Be.Foundation.Results;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Rendering;

/// <summary>Renders pages that show their content only through JavaScript; declared now, bound once the render companion
/// exists.</summary>
/// <remarks>Each call renders in a fresh context and persists nothing. It is for content, never a way past a challenge: a
/// challenge in the rendered page is an outcome like any other.</remarks>
public interface IBrowserRenderer
{
    /// <summary>Renders a page.</summary>
    /// <param name="request">The page to render.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The rendered page; <c>OperationTimeout</c> past the request's timeout, <c>ExternalUnavailable</c> when the
    /// browser crashed.</returns>
    Task<Result<RenderResult>> RenderAsync(RenderRequest request, CancellationToken cancellationToken = default);
}
