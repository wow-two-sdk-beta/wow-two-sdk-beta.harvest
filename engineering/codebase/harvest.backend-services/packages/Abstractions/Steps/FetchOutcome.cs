namespace WoW.Two.Sdk.Beta.Be.Harvest.Steps;

/// <summary>Refers to what a host or platform answered a step — the second of the three outcome levels.</summary>
/// <remarks>A challenge is never a parse failure and never a deletion; only <see cref="Gone"/> from an available source
/// is an authoritative negative.</remarks>
public enum FetchOutcome
{
    /// <summary>The content arrived.</summary>
    Ok,

    /// <summary>The target says it no longer exists (410, 404 on a direct fetch, a named not-found redirect) — a finding.</summary>
    Gone,

    /// <summary><c>robots.txt</c> disallows the path and the source obeys it.</summary>
    RobotsDisallowed,

    /// <summary>The source's permission does not allow scheduling.</summary>
    Restricted,

    /// <summary>A challenge stood in the way; the signal carries its kind.</summary>
    Challenge,

    /// <summary>A waiting room or queue stood in the way.</summary>
    WaitingRoom,

    /// <summary>The host refused (401, 403).</summary>
    Blocked,

    /// <summary>The host throttled (429, 503 with <c>Retry-After</c>); it paces and never promotes the lane.</summary>
    RateLimited,

    /// <summary>The page is a shell that needs a browser.</summary>
    RenderRequired,

    /// <summary>The connection failed.</summary>
    NetworkError,

    /// <summary>The fetch timed out.</summary>
    FetchTimeout,

    /// <summary>The host answered another error status.</summary>
    HttpError,

    /// <summary>The content was not what the step expected.</summary>
    UnexpectedContent,

    /// <summary>The response exceeded the size limit.</summary>
    ResponseTooLarge,

    /// <summary>A redirect left the source's scope or was unsafe.</summary>
    InvalidRedirect,

    /// <summary>The page matched no known shape; a sample is kept.</summary>
    Unrecognized,

    /// <summary>The route is not approved.</summary>
    NotApproved,

    /// <summary>The provider's run failed.</summary>
    ProviderFailed,

    /// <summary>A quota ran out.</summary>
    QuotaExhausted,

    /// <summary>The rung's estimate does not fit the budget.</summary>
    BudgetExceeded,

    /// <summary>A consumer-owned connection is no longer valid.</summary>
    ConnectionInvalid,

    /// <summary>No lease of a needed capability was free.</summary>
    CapabilityUnavailable,

    /// <summary>The host asked to sign in.</summary>
    LoginRequired,

    /// <summary>The session expired.</summary>
    SessionExpired,

    /// <summary>The solver failed.</summary>
    SolverFailed,

    /// <summary>The lane failed at its ceiling.</summary>
    RungCeiling,

    /// <summary>The item waits for an operator.</summary>
    HumanPending,
}
