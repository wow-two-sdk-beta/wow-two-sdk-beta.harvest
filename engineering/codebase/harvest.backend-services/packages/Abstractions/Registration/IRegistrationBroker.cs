using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Registration;

/// <summary>Integrates Harvest's registry — tenants, their API keys, containers, sources and pipelines — in process or
/// over the running service.</summary>
/// <remarks>
/// Every form implements this one contract, and <c>RegistrationBrokerConformance</c> runs against each. Failures are
/// <c>Validation</c> for a malformed request, <c>NotFound</c> for an unknown reference and <c>Conflict</c> for a
/// duplicate identity; the HTTP form adds <c>Unauthorized</c>, <c>Forbidden</c> and <c>ExternalUnavailable</c>.
/// </remarks>
public interface IRegistrationBroker
{
    /// <summary>Registers a tenant under a slug no other tenant holds.</summary>
    /// <param name="registration">The tenant to register.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The tenant; <c>Validation</c> for a malformed slug or name, <c>Conflict</c> for a taken slug.</returns>
    Task<Result<TenantModel>> RegisterTenantAsync(TenantRegistration registration, CancellationToken cancellationToken = default);

    /// <summary>Reads a tenant.</summary>
    /// <param name="tenantId">The tenant's identifier.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The tenant; <c>NotFound</c> when no tenant has the identifier.</returns>
    Task<Result<TenantModel>> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Lists every tenant, by slug.</summary>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The tenants.</returns>
    Task<Result<IReadOnlyList<TenantModel>>> ListTenantsAsync(CancellationToken cancellationToken = default);

    /// <summary>Issues an API key to a tenant; the secret is returned this once and only its hash is kept.</summary>
    /// <param name="tenantId">The tenant the key acts for.</param>
    /// <param name="issue">The key's name, scopes and expiry.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The key with its secret; <c>NotFound</c> for an unknown tenant, <c>Validation</c> for a blank name, an
    /// unknown or operator scope, or an expiry in the past.</returns>
    Task<Result<IssuedApiKeyModel>> IssueApiKeyAsync(Guid tenantId, ApiKeyIssue issue, CancellationToken cancellationToken = default);

    /// <summary>Reads an API key's metadata.</summary>
    /// <param name="keyId">The key's identifier.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The key; <c>NotFound</c> when no key has the identifier.</returns>
    Task<Result<ApiKeyModel>> GetApiKeyAsync(Guid keyId, CancellationToken cancellationToken = default);

    /// <summary>Lists a tenant's API keys, oldest first, revoked ones included.</summary>
    /// <param name="tenantId">The tenant.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The keys; <c>NotFound</c> for an unknown tenant.</returns>
    Task<Result<IReadOnlyList<ApiKeyModel>>> ListApiKeysAsync(Guid tenantId, CancellationToken cancellationToken = default);

    /// <summary>Revokes a tenant's API key; revoking a revoked key returns it unchanged.</summary>
    /// <param name="tenantId">The tenant the key acts for.</param>
    /// <param name="keyId">The key's identifier.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The revoked key; <c>NotFound</c> when the tenant holds no key with the identifier.</returns>
    Task<Result<ApiKeyModel>> RevokeApiKeyAsync(Guid tenantId, Guid keyId, CancellationToken cancellationToken = default);

    /// <summary>Registers the pool or a tenant's private container.</summary>
    /// <param name="registration">The container to register.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The container; <c>Validation</c> for a pool with a tenant or a private container without one,
    /// <c>NotFound</c> for an unknown tenant, <c>Conflict</c> for a second pool or a taken external reference.</returns>
    Task<Result<ContainerModel>> RegisterContainerAsync(ContainerRegistration registration, CancellationToken cancellationToken = default);

    /// <summary>Reads a container.</summary>
    /// <param name="containerId">The container's identifier.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The container; <c>NotFound</c> when no container has the identifier.</returns>
    Task<Result<ContainerModel>> GetContainerAsync(Guid containerId, CancellationToken cancellationToken = default);

    /// <summary>Lists containers, oldest first: one tenant's private containers, or every container when no tenant is
    /// given.</summary>
    /// <param name="tenantId">The tenant whose private containers to list; every container when null.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The containers; <c>NotFound</c> for an unknown tenant.</returns>
    Task<Result<IReadOnlyList<ContainerModel>>> ListContainersAsync(Guid? tenantId = null, CancellationToken cancellationToken = default);

    /// <summary>Registers a source in a container under a handle no other source of its family holds there.</summary>
    /// <param name="registration">The source to register.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The source; <c>NotFound</c> for an unknown container, <c>Validation</c> for a malformed handle, an
    /// authority outside 0–100, an unknown retention class or check policy, or a malformed capability policy,
    /// <c>Conflict</c> for a taken handle.</returns>
    Task<Result<SourceModel>> RegisterSourceAsync(SourceRegistration registration, CancellationToken cancellationToken = default);

    /// <summary>Reads a source.</summary>
    /// <param name="sourceId">The source's identifier.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The source; <c>NotFound</c> when no source has the identifier.</returns>
    Task<Result<SourceModel>> GetSourceAsync(Guid sourceId, CancellationToken cancellationToken = default);

    /// <summary>Lists a container's sources, by family and handle.</summary>
    /// <param name="containerId">The container.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The sources; <c>NotFound</c> for an unknown container.</returns>
    Task<Result<IReadOnlyList<SourceModel>>> ListSourcesAsync(Guid containerId, CancellationToken cancellationToken = default);

    /// <summary>Declares a pipeline over a source of the same container; it starts <see cref="PipelineState.Active"/>
    /// when the source's permission is open or granted, else <see cref="PipelineState.PendingApproval"/>.</summary>
    /// <param name="declaration">The pipeline to declare.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The pipeline; <c>NotFound</c> for an unknown container or source, <c>Validation</c> for a source in
    /// another container, a denied source, a filtered source without a scope, or malformed schedules, step overrides
    /// or limits, <c>Conflict</c> when a pipeline with the same container, source and scope key exists.</returns>
    Task<Result<PipelineModel>> DeclarePipelineAsync(PipelineDeclaration declaration, CancellationToken cancellationToken = default);

    /// <summary>Reads a pipeline with its schedule rows.</summary>
    /// <param name="pipelineId">The pipeline's identifier.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The pipeline; <c>NotFound</c> when no pipeline has the identifier.</returns>
    Task<Result<PipelineModel>> GetPipelineAsync(Guid pipelineId, CancellationToken cancellationToken = default);

    /// <summary>Lists a container's pipelines, oldest first.</summary>
    /// <param name="containerId">The container.</param>
    /// <param name="cancellationToken">The token that cancels the call.</param>
    /// <returns>The pipelines; <c>NotFound</c> for an unknown container.</returns>
    Task<Result<IReadOnlyList<PipelineModel>>> ListPipelinesAsync(Guid containerId, CancellationToken cancellationToken = default);
}
