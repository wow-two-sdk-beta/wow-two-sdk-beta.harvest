using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Represents what an adapter declares for a source: its steps and the step plan of each work kind.</summary>
public sealed record AdapterDescriptor
{
    /// <summary>Gets the canonical source URL when its registry handle is an identity rather than a hostname.
    /// Its origin bounds queued source targets; publisher paths and provider endpoints remain the adapter's responsibility.</summary>
    public string? SourceUrl { get; init; }

    /// <summary>Gets the shared pacing and health key. When supplied, it must match the source URL's host;
    /// otherwise the source URL's host, or the legacy registry handle, is used.</summary>
    public string? HostKey { get; init; }

    /// <summary>Gets the other hosts that serve the source's post images, so the image copy worker may fetch from them;
    /// null when the source's images come from its own host only.</summary>
    public SourceMediaHosts? Media { get; init; }

    /// <summary>Gets the steps the adapter offers.</summary>
    public required IReadOnlyList<StepDescriptor> Steps { get; init; }

    /// <summary>Gets each work kind's plan: the step keys it runs, in order, such as capture →
    /// <c>enumerate, detail, contact, media, extract</c>.</summary>
    public required IReadOnlyDictionary<WorkKind, IReadOnlyList<string>> Plans { get; init; }
}
