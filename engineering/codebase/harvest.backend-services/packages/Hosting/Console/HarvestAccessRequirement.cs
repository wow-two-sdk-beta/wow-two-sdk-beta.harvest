using Microsoft.AspNetCore.Authorization;

namespace Harvest.Api.Console;

/// <summary>Requires a live Harvest caller and, when named, one of its granted scopes.</summary>
public sealed record HarvestAccessRequirement(string? Scope = null, bool IsTenantRead = false) : IAuthorizationRequirement;
