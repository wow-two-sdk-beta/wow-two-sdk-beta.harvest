using Microsoft.AspNetCore.Authorization;

namespace Harvest.Api.Registration.Access;

/// <summary>Requires a live Harvest caller and, when named, one of its granted scopes.</summary>
/// <param name="Scope">The required scope, when specified.</param>
/// <param name="RequireOperator">Whether only platform operators can satisfy the requirement.</param>
/// <param name="AllowIdentitySession">Whether a bound browser session can satisfy the requirement without the scope.</param>
public sealed record HarvestAccessRequirement(
    string? Scope = null,
    bool RequireOperator = false,
    bool AllowIdentitySession = false) : IAuthorizationRequirement;
