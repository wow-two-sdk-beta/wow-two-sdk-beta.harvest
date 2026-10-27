using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;

/// <summary>Keeps a run independent from subsequent registry edits.</summary>
internal sealed record FrozenRun(PipelineModel Pipeline, SourceModel Source, string AdapterKey, AdapterDescriptor Adapter);
