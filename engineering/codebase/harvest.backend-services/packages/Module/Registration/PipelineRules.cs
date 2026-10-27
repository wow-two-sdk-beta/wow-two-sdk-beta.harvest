using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Registration;

/// <summary>Holds the rules that tie a pipeline to its source.</summary>
internal static class PipelineRules
{
    /// <summary>Validates that the source can carry the pipeline: same container, not denied, and a scope for a filtered
    /// source.</summary>
    public static AppError? ValidateSource(PipelineDeclaration declaration, Guid sourceContainerId, SourcePermission permission, SourceCoverage coverage)
    {
        if (sourceContainerId != declaration.ContainerId)
            return RegistrationErrors.Invalid("A pipeline harvests a source of its own container.");
        if (permission == SourcePermission.Denied)
            return RegistrationErrors.Invalid("The source's permission is denied; nothing may be scheduled over it.");

        return coverage == SourceCoverage.Filtered
               && RegistrationRules.CanonicalScopeKey(declaration.Scope).ScopeKey == PipelineConstants.WholeScopeKey
            ? RegistrationErrors.Invalid("A filtered source needs a scope: a listing URL or filter parameters.")
            : null;
    }

    /// <summary>Returns a new pipeline's state: active over an open or granted source, else waiting for approval.</summary>
    public static PipelineState InitialState(SourcePermission permission) =>
        permission is SourcePermission.Open or SourcePermission.Granted ? PipelineState.Active : PipelineState.PendingApproval;
}
