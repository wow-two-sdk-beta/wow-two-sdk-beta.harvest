using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;

/// <summary>A reviewed public directory to register for Ocharo's candidate stream.</summary>
/// <param name="Key">The private adapter key.</param>
/// <param name="Name">The source display name.</param>
/// <param name="Seeds">Exact public listing URLs; no inferred pagination.</param>
/// <param name="Authority">Evidence proximity, rather than a claim of access or accuracy.</param>
public sealed record OcharoSourcePreset(string Key, string Name, IReadOnlyList<string> Seeds, int Authority)
{
    /// <summary>The source family; the opt-in Instagram preset shares one platform source across publisher pipelines.</summary>
    public SourceFamily Family { get; init; } = SourceFamily.Site;

    private static readonly IReadOnlyList<OcharoSourcePreset> Defaults = OcharoSourceRegistry.DefaultPresets();

    /// <summary>The original opt-in publisher profiles, selected from the source registry.</summary>
    public static OcharoSourcePreset Instagram { get; } = Defaults.Single(preset => preset.Family == SourceFamily.Instagram);

    /// <summary>The website presets used by callers that do not select publisher groups.</summary>
    public static IReadOnlyList<OcharoSourcePreset> All { get; } = Defaults.Where(preset => preset.Family == SourceFamily.Site).ToArray();
}
