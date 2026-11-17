using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Identifies the saved reading and exposes its selected fields to a processor.</summary>
/// <param name="ContainerId">The container that owns the reading.</param>
/// <param name="Source">The source and its processing policy.</param>
/// <param name="PostId">The post associated with this reading.</param>
/// <param name="LinkId">The source link.</param>
/// <param name="CaptureId">The captured material that produced the reading.</param>
/// <param name="Title">The saved title.</param>
/// <param name="Text">The saved description or text.</param>
/// <param name="Language">The language inferred for the saved reading.</param>
/// <param name="Extras">The saved source extras.</param>
/// <param name="Model">The optional model route for this processor.</param>
/// <param name="Trace">The operation recorder for the worker claim.</param>
public sealed record CaptureProcessInput(Guid ContainerId, SourceModel Source, Guid PostId, Guid LinkId, Guid CaptureId,
    string? Title, string? Text, PostLanguage? Language, IReadOnlyDictionary<string, JsonElement> Extras,
    string? Model, ICaptureProcessTrace Trace);
