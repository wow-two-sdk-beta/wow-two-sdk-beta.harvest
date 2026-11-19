using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Ai.Runs;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

/// <summary>Answers whether post writes queue tagging: the host enabled it and composed the shared AI runner.</summary>
internal sealed class PostTagsGate(IOptions<PostTagsOptions> options, IServiceProviderIsService services)
{
    private readonly Lazy<bool> runner = new(() => services.IsService(typeof(IAiRunner)));

    /// <summary>Gets whether tagging is on in this host.</summary>
    internal bool Enabled => options.Value.Enabled && runner.Value;
}
