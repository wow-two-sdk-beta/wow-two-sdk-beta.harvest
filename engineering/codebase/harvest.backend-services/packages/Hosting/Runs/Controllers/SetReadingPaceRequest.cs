namespace Harvest.Api.Runs.Controllers;

/// <summary>Sets the least gap between two requests to a source's host.</summary>
public sealed record SetReadingPaceRequest
{
    /// <summary>Gets the gap in milliseconds, 1000–60000.</summary>
    public required int MinDelayMs { get; init; }
}
