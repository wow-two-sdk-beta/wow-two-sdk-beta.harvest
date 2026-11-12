using System.Text.Json;
using System.Text.Json.Nodes;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Afisha;

/// <summary>Declares Afisha's public category, article and known-link check steps.</summary>
public sealed class AfishaSourceAdapter : ISourceAdapter, ISourceConfigurationProvider
{
    /// <inheritdoc />
    public SourceConfigurationModel DescribeConfiguration(SourceModel source)
    {
        var schema = JsonNode.Parse(SourceScopeSchemas.WebUrl(
            "Choose an Afisha section or save a specific announcement URL.").GetRawText())!.AsObject();
        schema["sections"] = JsonSerializer.SerializeToNode(
            from locale in new[] { "ru", "uz" }
            from section in AfishaUrls.Sections
            select new { label = section.Value, url = $"https://{source.Handle}/{locale}/{section.Key}", locale });
        return new()
        {
            DisplayName = "Afisha Uzbekistan",
            Message = "The source adapter is installed. Remote access is evaluated during each run.",
            ScopeSchema = JsonSerializer.SerializeToElement(schema),
        };
    }

    /// <inheritdoc />
    public string Key => "afisha-uz";

    /// <inheritdoc />
    public SourceFamily Family => SourceFamily.Site;

    /// <inheritdoc />
    public bool Serves(SourceModel source)
    {
        return source.Family == Family && AfishaUrls.IsHost(source.Handle);
    }

    /// <inheritdoc />
    public AdapterDescriptor Describe(SourceModel source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var requirements = new StepRequirements { MinDelay = TimeSpan.FromSeconds(2) };
        return new AdapterDescriptor
        {
            Steps =
            [
                new() { Key = "enumerate", Role = StepRole.Enumerate, Requires = requirements },
                new() { Key = "detail", Role = StepRole.Detail, Requires = requirements },
                new() { Key = "check", Role = StepRole.Check, Requires = requirements },
            ],
            Plans = new Dictionary<WorkKind, IReadOnlyList<string>>
            {
                [WorkKind.Capture] = ["enumerate", "detail"],
                [WorkKind.Discover] = ["enumerate", "detail"],
                [WorkKind.Sweep] = ["enumerate", "detail"],
                [WorkKind.Refresh] = ["detail"],
                [WorkKind.Link] = ["detail"],
                [WorkKind.Check] = ["check"],
            },
        };
    }
}
