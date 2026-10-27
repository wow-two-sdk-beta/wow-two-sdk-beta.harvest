using WoW.Two.Sdk.Beta.Be.Harvest.Containers;

namespace Harvest.Api.Registration.Access;

/// <summary>Represents who a request acts for: the operator, or one tenant.</summary>
/// <param name="IsOperator">Whether the operator key authenticated the request.</param>
/// <param name="TenantId">The tenant a tenant key acts for; null for the operator.</param>
public sealed record Caller(bool IsOperator, Guid? TenantId)
{
    /// <summary>Tells whether the caller may read a container: the operator any, a tenant the pool and its own.</summary>
    /// <param name="container">The container.</param>
    public bool CanRead(ContainerModel container) =>
        IsOperator || container.Kind == ContainerKind.Pool || container.TenantId == TenantId;

    /// <summary>Tells whether the caller may declare in a container: the operator any, a tenant its private ones.</summary>
    /// <param name="container">The container.</param>
    public bool CanWrite(ContainerModel container) =>
        IsOperator || (container.Kind == ContainerKind.Private && container.TenantId == TenantId);
}
