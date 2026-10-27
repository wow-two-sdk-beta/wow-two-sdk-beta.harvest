namespace Harvest.Api.Runs.Controllers;

/// <summary>Changes a pipeline's operator pause switch.</summary>
public sealed record PausePipelineRequest
{
    /// <summary>Gets whether the pipeline should pause.</summary>
    public required bool Paused { get; init; }
}
