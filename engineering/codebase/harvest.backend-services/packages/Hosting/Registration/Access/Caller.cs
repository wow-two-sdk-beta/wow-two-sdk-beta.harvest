using WoW.Two.Sdk.Beta.Be.Harvest.Containers;

namespace Harvest.Api.Registration.Access;

/// <summary>Represents who a request acts for: the operator, or one tenant.</summary>
/// <param name="IsOperator">Whether the caller has an operator binding.</param>
/// <param name="TenantId">The tenant the caller acts for, or null for an operator.</param>
public sealed record Caller(bool IsOperator, Guid? TenantId)
{
    /// <summary>Gets the scopes granted to this authenticated caller.</summary>
    public IReadOnlyList<string> Scopes { get; init; } = [];

    /// <summary>Gets whether a validated Identity browser session supplies this caller's permissions.</summary>
    public bool IsIdentitySession { get; init; }

    /// <summary>Tells whether the caller may read a container: the operator any, a tenant the pool and its own.</summary>
    /// <param name="container">The container.</param>
    public bool CanRead(ContainerModel container) =>
        IsOperator || container.Kind == ContainerKind.Pool || container.TenantId == TenantId;

    /// <summary>Tells whether the caller may declare in a container: the operator any, a tenant its private ones.</summary>
    /// <param name="container">The container.</param>
    public bool CanWrite(ContainerModel container) =>
        IsOperator || (container.Kind == ContainerKind.Private && container.TenantId == TenantId);
}
