namespace WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;

/// <summary>Represents a captcha to solve, with the exit and user agent its token may be bound to.</summary>
public sealed record ChallengeSolveRequest
{
    /// <summary>Gets the kind of captcha.</summary>
    public required ChallengeKind Kind { get; init; }

    /// <summary>Gets the page the captcha sits on.</summary>
    public required Uri PageUrl { get; init; }

    /// <summary>Gets the widget's site key, when it has one.</summary>
    public string? SiteKey { get; init; }

    /// <summary>Gets the egress lease whose exit the solve should pass through.</summary>
    public Guid? EgressLeaseId { get; init; }

    /// <summary>Gets the user agent the token will be presented with.</summary>
    public string? UserAgent { get; init; }
}
