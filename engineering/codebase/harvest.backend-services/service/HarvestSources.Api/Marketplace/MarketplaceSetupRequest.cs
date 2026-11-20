using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace HarvestSources.Api.Marketplace;

/// <summary>Explicit marketplace setup options; manual runs stay available with disabled schedules.</summary>
public sealed record MarketplaceSetupRequest
{
    /// <summary>Starts the profile's capture and check cadences, on existing pipelines too.</summary>
    public bool EnableSchedules { get; init; }

    /// <summary>Issues the consumer's <c>events:read</c> key when it holds no active one.</summary>
    public bool IssueKey { get; init; }

    /// <summary>Revokes the consumer's active keys and issues one.</summary>
    public bool RotateKey { get; init; }

    /// <summary>Opens the profile's closed collected sources to model processing <c>auto</c>, and leaves a <c>manual</c> or
    /// <c>auto</c> one alone. The older form of <see cref="ModelProcessing"/>; asking for both is refused unless it is <c>auto</c>.</summary>
    public bool AllowAi { get; init; }

    /// <summary>Sets the model-processing mode, <c>off</c>, <c>manual</c> or <c>auto</c>, on every collected source of the profile; none
    /// keeps each as it is.</summary>
    public ModelProcessingMode? ModelProcessing { get; init; }
}
