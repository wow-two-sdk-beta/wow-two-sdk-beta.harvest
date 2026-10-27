using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Posts;

/// <summary>Represents an operator's capture write over the HTTP form.</summary>
public sealed record SaveCaptureRequest
{
    /// <summary>Gets the owning container.</summary>
    public required Guid ContainerId { get; init; }
    /// <summary>Gets the registered source.</summary>
    public required Guid SourceId { get; init; }
    /// <summary>Gets the run that made the capture, when one exists.</summary>
    public Guid? RunId { get; init; }
    /// <summary>Gets the capture to store.</summary>
    public required Capture Capture { get; init; }
    /// <summary>Gets its extracted fields.</summary>
    public required PostEnvelope Envelope { get; init; }
}
