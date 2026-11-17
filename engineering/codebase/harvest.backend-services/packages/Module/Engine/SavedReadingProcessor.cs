using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Engine;

/// <summary>Produces a model-free summary of the fields the reader saved.</summary>
internal sealed class SavedReadingProcessor : ICaptureProcessor
{
    public string Key => ICaptureProcessor.DefaultKey;

    public Task<CaptureProcessResult> ProcessAsync(CaptureProcessInput input, CancellationToken cancellationToken = default)
    {
        JsonElement? listing = input.Extras.TryGetValue("listing", out var facts) ? facts : null;
        var output = JsonSerializer.SerializeToElement(new
        {
            schema = Key, title = input.Title, listing,
            hasDescription = !string.IsNullOrWhiteSpace(input.Text),
        }, JsonOptionsConstants.Default);
        return Task.FromResult(new CaptureProcessResult(new StepResult { Outcome = FetchOutcome.Ok }, output));
    }
}
