using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Sessions;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Represents the session a step needs.</summary>
public sealed record SessionNeed
{
    /// <summary>Gets whose session the step carries.</summary>
    public SessionKind Kind { get; init; } = SessionKind.Anonymous;

    /// <summary>Gets how long an anonymous session lives.</summary>
    public SessionScope Scope { get; init; } = SessionScope.Run;

    /// <summary>Gets the account pool an account session comes from.</summary>
    public string? Pool { get; init; }
}
