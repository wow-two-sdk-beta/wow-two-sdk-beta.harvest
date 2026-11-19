using Harvest.Api.Registration.Access;
using Microsoft.AspNetCore.Authorization;
using WoW.Two.Sdk.Beta.Be.Ai.Hosting;
using WoW.Two.Sdk.Beta.Be.Ai.Hosting.Access;
using WoW.Two.Sdk.Beta.Be.Ai.Module;

namespace HarvestSources.Api.Configurations;

/// <summary>Mounts the shared AI module's HTTP API (<c>api/ai/</c>: use cases, prompts, the inference log, eval sets, runs
/// and reports) on the internal source host whose shared AI is on (<see cref="RentalTermsConfiguration.EnabledSetting"/>).</summary>
/// <remarks>
///   - every route keeps its own <c>ai.*</c> scope (<see cref="AiApiPolicies"/>) and needs a live SCFD caller as well
///   - an SCFD operator holds every <c>ai.*</c> scope, as it holds every registry scope; a tenant key holds none, so a tenant
///     reaches no AI route; a service token reaches one when it lists the route's scope
///   - data scopes stay the AI package's default: the unscoped bucket, where the passes' calls and eval sets live
///   - the AI eval worker runs queued eval matrices beside the engine; <c>Harvest:Worker:Enabled=false</c> stops it too
/// </remarks>
internal static class AiApiConfiguration
{
    /// <summary>Holds the section of the AI eval worker's settings: poll interval, orphan threshold, concurrency.</summary>
    internal const string EvalWorkerSection = "Ai:EvalWorker";

    internal static void AddAiApiForOperators(this WebApplicationBuilder builder)
    {
        if (!RentalTermsConfiguration.IsEnabled(builder.Configuration))
            return;

        builder.Services.AddAiApi().BindConfiguration(AiApiOptions.SectionName);
        builder.Services.AddScoped<IAuthorizationHandler, OperatorAiScopeHandler>();
        // Queued eval matrices (models x prompt versions) run here; Harvest:Worker:Enabled=false stops it with the engine.
        builder.Services.AddAiEvalWorker().BindConfiguration(EvalWorkerSection)
            .PostConfigure<IConfiguration>((options, configuration) => options.Enabled &= configuration.GetValue("Harvest:Worker:Enabled", true));
    }

    internal static void MapAiApiForOperators(this WebApplication app)
    {
        if (!RentalTermsConfiguration.IsEnabled(app.Configuration))
            return;

        app.MapAiApi().RequireAuthorization(AccessPolicies.AnyCaller);
    }

    /// <summary>Grants an <c>ai.*</c> scope to an SCFD operator: the operator key, an operator's console session or an
    /// operator service grant.</summary>
    private sealed class OperatorAiScopeHandler(CallerResolver callers, IHttpContextAccessor contexts) : AuthorizationHandler<AiScopeRequirement>
    {
        protected override async Task HandleRequirementAsync(AuthorizationHandlerContext context, AiScopeRequirement requirement)
        {
            var cancellationToken = contexts.HttpContext?.RequestAborted ?? CancellationToken.None;
            if (context.User.Identity?.IsAuthenticated == true && await callers.ResolveAsync(context.User, cancellationToken) is { IsOperator: true })
                context.Succeed(requirement);
        }
    }
}
