using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence.Rows;

/// <summary>Maps a row of <c>harvest_pipelines</c>.</summary>
internal sealed class PipelineRow
{
    public const string TableName = "harvest_pipelines";

    public required Guid Id { get; set; }

    public required Guid ContainerId { get; set; }

    public required Guid SourceId { get; set; }

    public required string ScopeKey { get; set; }

    /// <summary>Gets or sets the jsonb scope as declared.</summary>
    public required string Scope { get; set; }

    public string? Name { get; set; }

    public required PipelineState State { get; set; }

    public PipelinePauseReason? PauseReason { get; set; }

    public required DateTimeOffset StateSince { get; set; }

    public required bool Pinned { get; set; }

    public required AiMode AiMode { get; set; }

    /// <summary>Gets or sets the jsonb per-run limits.</summary>
    public required string Limits { get; set; }

    /// <summary>Gets or sets the jsonb step overrides, by step key.</summary>
    public required string StepOverrides { get; set; }

    public string? CreatedBy { get; set; }

    public required DateTimeOffset CreatedAt { get; set; }
}
