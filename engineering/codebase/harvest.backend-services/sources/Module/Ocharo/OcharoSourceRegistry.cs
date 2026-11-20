using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Caex;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;

/// <summary>The reviewed source registry. Listing it has no network or storage side effects.</summary>
public sealed partial record OcharoSourceRegistry
{
    // A generic-* key is the key of that family's generic adapter from AddHarvestReaders(). A source reaches its adapter
    // by family, never by ExternalRef (which only carries the profile key): the generic adapter of the profile's family
    // serves it, unless a named adapter serves its host, and the named one wins. A generic-site channel on a host a
    // named reader owns therefore runs that reader.
    private static readonly CollectionProfile[] Profiles =
    [
        new("afisha-uz", "Afisha", 50, SourceFamily.Site, "web-direct"),
        new("anons-uz", "ANONS", 50, SourceFamily.Site, "web-direct", MaxPages: 25),
        new("craftfair-uz", "Craftfair / Hunarmand", 80, SourceFamily.Site, "web-direct"),
        new("uzexpocentre-uz", "Uzexpocentre", 80, SourceFamily.Site, "none"),
        new(CaexSourceAdapter.AdapterKey, "CAEx", 80, SourceFamily.Site, "none"),
        new("generic-feed", "Generic feed", 60, SourceFamily.Feed, "none"),
        new("generic-structured", "Generic structured page", 60, SourceFamily.Structured, "web-direct"),
        new("generic-site", "Generic site", 60, SourceFamily.Site, "web-direct"),
        new(InstagramSourceAdapter.AdapterKey, "Instagram", 80, SourceFamily.Instagram, "none"),
    ];

    /// <summary>The registry format version.</summary>
    public required string Format { get; init; }

    /// <summary>The date of the source and channel review, not the date of a harvest.</summary>
    public required DateOnly ReviewedOn { get; init; }

    /// <summary>The ranked source groups and their channels.</summary>
    public required IReadOnlyList<OcharoSourceGroup> Groups { get; init; }

    /// <summary>The registry compiled into this version of the private source host.</summary>
    public static OcharoSourceRegistry Current { get; } = Load();

    internal IEnumerable<CollectionRoute> Routes(bool includeInstagram, IReadOnlySet<string>? groupIds = null)
    {
        foreach (CollectionProfile profile in Profiles)
        {
            bool instagram = profile.Family == SourceFamily.Instagram;
            if (instagram && !includeInstagram)
                continue;
            // Keep the existing preset order for callers that do not select groups.
            IEnumerable<OcharoSourceGroup> groups = instagram && groupIds is null
                ? Groups.OrderBy(group => group.Id == "vfw-tashkent" ? 0 : 1) : Groups;
            foreach (OcharoSourceGroup group in groups)
            {
                if (groupIds is not null && !groupIds.Contains(group.Id))
                    continue;
                foreach (OcharoSourceChannel channel in group.Channels)
                    if (channel.AdapterKey == profile.Key && (groupIds is not null || channel.Setup == "default"))
                        yield return new(group, channel, profile);
            }
        }
    }

    internal static IReadOnlyList<OcharoSourcePreset> DefaultPresets() => Current.Routes(includeInstagram: true)
        .GroupBy(route => route.Profile)
        .Select(routes => new OcharoSourcePreset(routes.Key.Key, routes.Key.Name,
            routes.Select(route => route.Channel.PipelineUrl).ToArray(), routes.Key.Authority) { Family = routes.Key.Family })
        .ToArray();

    private static OcharoSourceRegistry Load()
    {
        using Stream stream = typeof(OcharoSourceRegistry).Assembly.GetManifestResourceStream("Ocharo.EventSources.json")
            ?? throw new InvalidOperationException("The Ocharo event-source registry is missing.");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        var registry = JsonSerializer.Deserialize<OcharoSourceRegistry>(stream, options)
            ?? throw new InvalidOperationException("The Ocharo event-source registry is empty.");
        registry.Validate();
        return registry;
    }

    internal void Validate()
    {
        var groupIds = Groups.Select(group => group.Id).ToHashSet(StringComparer.Ordinal);
        if (Format != "ocharo.event-sources.v1" || Groups.Count == 0 || groupIds.Count != Groups.Count
            || !Groups.Select(group => group.Rank).SequenceEqual(Enumerable.Range(1, Groups.Count)))
            throw new InvalidOperationException("The Ocharo event-source registry format, ranks or group IDs are invalid.");

        var seeds = new HashSet<string>(StringComparer.Ordinal);
        foreach (OcharoSourceGroup group in Groups)
        {
            if (!Identifier().IsMatch(group.Id) || group.Tier is not ("P1" or "P2" or "P3")
                || group.Channels.Count(channel => channel.Primary) != 1
                || group.Channels.Select(channel => channel.Id).Distinct(StringComparer.Ordinal).Count() != group.Channels.Count
                || group.Aggregates.Any(id => id == group.Id || !groupIds.Contains(id)))
                throw new InvalidOperationException($"Source group '{group.Id}' is invalid.");

            foreach (OcharoSourceChannel channel in group.Channels)
            {
                CollectionProfile? profile = Profiles.SingleOrDefault(item => item.Key == channel.AdapterKey);
                bool supported = channel.Mode is "harvest" or "provider";
                if (!Identifier().IsMatch(channel.Id) || !PublicUrl(channel.Url)
                    || channel.Relationship is not ("owned" or "related" or "cross-check" or "route")
                    || channel.Mode is not ("harvest" or "provider" or "planned" or "manual" or "reference")
                    || (channel.RelatedGroupId is { } related && (related == group.Id || !groupIds.Contains(related)))
                    || (channel.EventFamily is { } family && !group.EventFamilies.Contains(family))
                    || (supported && (profile is null || channel.Relationship != "owned"
                        || (channel.Mode == "provider") != (profile.Family == SourceFamily.Instagram)
                        || (profile.Family == SourceFamily.Instagram && (string.IsNullOrWhiteSpace(channel.DisplayName)
                            || !InstagramPublishers.TryReadProfile(channel.PipelineUrl, out _, out _)))
                        || channel.Setup is not ("default" or "selected")
                        || !PublicUrl(channel.PipelineUrl, requireHttps: true)
                        || !seeds.Add(new Uri(channel.PipelineUrl).AbsoluteUri)))
                    || (!supported && (channel.AdapterKey is not null || channel.Seed is not null || channel.Setup is not null)))
                    throw new InvalidOperationException($"Channel '{group.Id}/{channel.Id}' is invalid.");
            }
        }
    }

    private static bool PublicUrl(string value, bool requireHttps = false) =>
        Uri.TryCreate(value, UriKind.Absolute, out Uri? url) && url.UserInfo.Length == 0
        && (url.Scheme == Uri.UriSchemeHttps || !requireHttps && url.Scheme == Uri.UriSchemeHttp);

    [GeneratedRegex("^[a-z0-9]+(?:-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();

    /// <summary>A publisher's collection settings. <paramref name="MaxPages"/> bounds a run's listing and detail pages
    /// together; a publisher whose listing holds more events than fit in five pages takes a larger bound.</summary>
    internal sealed record CollectionProfile(string Key, string Name, int Authority, SourceFamily Family, string CheckPolicy,
        int MaxPages = 5)
    {
        /// <summary>Gets when the profile's capture runs; every profile takes <see cref="CaptureCadence.Default"/> unless it names its own.</summary>
        internal CaptureCadence Cadence { get; init; } = CaptureCadence.Default;
    }

    /// <summary>When a profile's capture runs, as the schedule row the provisioner declares for it.</summary>
    /// <param name="Mode">How the row decides when its work is due.</param>
    /// <param name="Cron">The cron expression for <see cref="ScheduleMode.Cron"/>.</param>
    /// <param name="Every">The interval for <see cref="ScheduleMode.Interval"/>.</param>
    /// <param name="TimeZone">The IANA time zone the cron expression reads in.</param>
    internal sealed record CaptureCadence(ScheduleMode Mode, string? Cron = null, TimeSpan? Every = null, string TimeZone = "Asia/Tashkent")
    {
        /// <summary>Gets four captures a day, at 01:00, 07:00, 13:00 and 19:00 Tashkent time, so the posts of a window reach
        /// tagging as one burst.</summary>
        internal static CaptureCadence Default { get; } = new(ScheduleMode.Cron, "0 1,7,13,19 * * *");

        /// <summary>Returns the capture schedule row, live or disabled.</summary>
        internal ScheduleDeclaration Declare(bool enabled) => new()
        {
            Kind = WorkKind.Capture, Mode = Mode, Cron = Cron, Every = Every, TimeZone = TimeZone, Enabled = enabled,
        };
    }

    internal sealed record CollectionRoute(OcharoSourceGroup Group, OcharoSourceChannel Channel, CollectionProfile Profile);
}
