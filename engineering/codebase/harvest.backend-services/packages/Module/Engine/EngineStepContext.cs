using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Challenges;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Rendering;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;

/// <summary>Collects observations from one claimed step without making network calls inside a transaction.</summary>
internal sealed class EngineStepContext(
    ResolvedPlan plan,
    SourceModel source,
    HttpClient http,
    IChallengeDetector challenges,
    TimeProvider clock,
    IBrowserRenderer? browser,
    PipelineScope scope) : IStepContext
{
    public ResolvedPlan Plan { get; } = plan;
    public SourceModel Source { get; } = source;
    public HttpClient Http { get; } = http;
    public IChallengeDetector Challenges { get; } = challenges;
    public TimeProvider Clock { get; } = clock;
    public IBrowserRenderer? Browser { get; } = browser;
    internal List<StepSignal> Signals { get; } = [];

    public bool AllowsDestination(Uri destination) =>
        !scope.RestrictToPath || RunPlanner.InScope(destination.AbsoluteUri, Source, scope);

    public void Observe(StepSignal signal)
    {
        Signals.Add(signal);
    }
}
