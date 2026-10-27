using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

namespace Harvest.Api.Runs.Controllers;

/// <summary>Identifies a pipeline and the work to queue.</summary>
public sealed record StartRunRequest
{
    /// <summary>Gets the pipeline identifier.</summary>
    public required Guid PipelineId { get; init; }
    /// <summary>Gets the requested work.</summary>
    public WorkKind Kind { get; init; } = WorkKind.Capture;
}
