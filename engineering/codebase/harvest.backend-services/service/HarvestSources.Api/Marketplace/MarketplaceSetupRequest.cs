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
}
