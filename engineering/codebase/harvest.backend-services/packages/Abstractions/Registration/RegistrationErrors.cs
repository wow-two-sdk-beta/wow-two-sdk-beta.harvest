using WoW.Two.Sdk.Beta.Be.Foundation.Errors;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Registration;

/// <summary>Holds the error catalogue of the registration contract.</summary>
public static class RegistrationErrors
{
    /// <summary>Creates the error for a malformed request.</summary>
    /// <param name="message">What is malformed.</param>
    public static AppError Invalid(string message) => AppErrorFactory.Validation(message);

    /// <summary>Creates the error for an unknown tenant.</summary>
    /// <param name="tenantId">The identifier no tenant has.</param>
    public static AppError TenantNotFound(Guid tenantId) => AppErrorFactory.NotFound($"No tenant has the id {tenantId}.");

    /// <summary>Creates the error for a slug another tenant holds.</summary>
    /// <param name="slug">The taken slug.</param>
    public static AppError TenantSlugTaken(string slug) => AppErrorFactory.Conflict($"A tenant with the slug '{slug}' exists.");

    /// <summary>Creates the error for an unknown API key.</summary>
    /// <param name="keyId">The identifier no key has.</param>
    public static AppError ApiKeyNotFound(Guid keyId) => AppErrorFactory.NotFound($"No API key has the id {keyId}.");

    /// <summary>Creates the error for an unknown container.</summary>
    /// <param name="containerId">The identifier no container has.</param>
    public static AppError ContainerNotFound(Guid containerId) => AppErrorFactory.NotFound($"No container has the id {containerId}.");

    /// <summary>Creates the error for a second pool.</summary>
    public static AppError PoolExists() => AppErrorFactory.Conflict("The instance already has its pool container.");

    /// <summary>Creates the error for an external reference the tenant already uses.</summary>
    /// <param name="externalRef">The taken reference.</param>
    public static AppError ContainerRefTaken(string externalRef) =>
        AppErrorFactory.Conflict($"The tenant already has a container named '{externalRef}'.");

    /// <summary>Creates the error for an unknown source.</summary>
    /// <param name="sourceId">The identifier no source has.</param>
    public static AppError SourceNotFound(Guid sourceId) => AppErrorFactory.NotFound($"No source has the id {sourceId}.");

    /// <summary>Creates the error for a handle another source of the family holds in the container.</summary>
    /// <param name="handle">The taken handle.</param>
    public static AppError SourceExists(string handle) =>
        AppErrorFactory.Conflict($"The container already has a source with the handle '{handle}' in this family.");

    /// <summary>Creates the error for an unknown pipeline.</summary>
    /// <param name="pipelineId">The identifier no pipeline has.</param>
    public static AppError PipelineNotFound(Guid pipelineId) => AppErrorFactory.NotFound($"No pipeline has the id {pipelineId}.");

    /// <summary>Creates the error for a pipeline identity that exists.</summary>
    /// <param name="scopeKey">The scope key both share.</param>
    public static AppError PipelineExists(string scopeKey) =>
        AppErrorFactory.Conflict($"A pipeline over this source with the scope '{scopeKey}' exists; join it instead.");
}
