using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Extraction;

/// <summary>Turns a capture into the base envelope through the tier cascade, so every capture yields one envelope.</summary>
public interface IPostExtractor
{
    /// <summary>Extracts a capture.</summary>
    /// <param name="capture">The capture to read.</param>
    /// <param name="request">The source family and the AI gates.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The envelope; a failure only when the deterministic tier cannot read the capture at all.</returns>
    Task<Result<PostEnvelope>> ExtractAsync(Capture capture, ExtractionRequest request, CancellationToken cancellationToken = default);
}
