using System.Text.Json;
using System.Text.Json.Serialization;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;

/// <summary>Ocharo's one selector subscription as the image ships it: the clauses Ocharo follows and the registry
/// publishers whose posts count, read from <c>ocharo-selector.json</c>. Listing it has no network or storage side effects.</summary>
/// <remarks>The file names publishers by registry id, or follows the whole registry (<see cref="FollowRegistry"/>);
/// <see cref="Resolve"/> turns them into the source groups of one container, so the same file serves every target. A
/// client's later edits to the stored selector are never overwritten unless setup is asked to reset it.</remarks>
public sealed record OcharoSelector
{
    private const string FormatName = "ocharo-selector@1";

    /// <summary>Gets the file's format version.</summary>
    public required string Format { get; init; }

    /// <summary>Gets the selector subscription's name, which Events binds an intake source to as <c>selector:{name}</c>.</summary>
    public required string Name { get; init; }

    /// <summary>Gets the consumer schema the subscription asks for.</summary>
    public required string Schema { get; init; }

    /// <summary>Gets the subscription's own routing context, such as <c>catalog=ocharo-fashion</c>.</summary>
    public IReadOnlyDictionary<string, string> Context { get; init; } = new Dictionary<string, string>();

    /// <summary>Gets the tag clauses, with no source refs: the publishers are <see cref="Sources"/> until
    /// <see cref="Resolve"/> binds them to a container.</summary>
    public required SubscriptionSelector Selector { get; init; }

    /// <summary>Gets whether the selector follows every registry publisher the container provisions, not only the ones
    /// <see cref="Sources"/> list. <see cref="Resolve"/> then binds each source group of the container whose name is a
    /// registry publisher's, in the registry's rank order, and a <see cref="Sources"/> entry only adds its context to its
    /// publisher. False, the default, binds exactly the listed publishers.</summary>
    public bool FollowRegistry { get; init; }

    /// <summary>Gets the registry publishers the selector follows, in the order their refs are written; with
    /// <see cref="FollowRegistry"/> the entries are per-publisher context overrides, such as <c>defaultKind</c>, and the
    /// list may be empty.</summary>
    public required IReadOnlyList<OcharoSelectorSource> Sources { get; init; }

    /// <summary>Gets the selector compiled into this version of the private source host.</summary>
    public static OcharoSelector Current { get; } = Load();

    /// <summary>Returns the selector with each publisher bound to the container's source group of that name, adding the
    /// registry's <c>sourceGroup</c> (id) and <c>sourceName</c> (publisher name) to that ref's context.</summary>
    /// <param name="groups">The source groups of the Ocharo container; a publisher is matched by the name the registry gives it,
    /// without regard to case.</param>
    /// <returns>The selector to subscribe with. A listed selector binds the publishers <see cref="Sources"/> list, in that
    /// order, and answers a validation error naming the first one the container lacks. A selector that follows the
    /// registry binds every provisioned publisher in registry rank order, skips a listed one that is not provisioned and
    /// answers a validation error when the container holds none.</returns>
    public Result<SubscriptionSelector> Resolve(IReadOnlyList<SourceGroupModel> groups)
    {
        ArgumentNullException.ThrowIfNull(groups);
        return FollowRegistry ? ResolveRegistry(groups) : ResolveListed(groups);
    }

    private Result<SubscriptionSelector> ResolveListed(IReadOnlyList<SourceGroupModel> groups)
    {
        var include = new List<SelectorSourceRef>();
        foreach (var source in Sources)
        {
            var publisher = OcharoSourceRegistry.Current.Groups.Single(group => group.Id == source.Group);
            var group = groups.FirstOrDefault(item => string.Equals(item.Name, publisher.Name, StringComparison.OrdinalIgnoreCase));
            if (group is null)
                return Result<SubscriptionSelector>.Fail(AppErrorFactory.Validation(
                    $"Group {source.Group} is not provisioned in the Ocharo container; run setup with --group {source.Group}."));
            include.Add(Bind(group, publisher, source.Context));
        }
        return Result<SubscriptionSelector>.Ok(Selector with { Sources = new SelectorSources { Include = include } });
    }

    private Result<SubscriptionSelector> ResolveRegistry(IReadOnlyList<SourceGroupModel> groups)
    {
        var overrides = Sources.ToDictionary(source => source.Group, source => source.Context, StringComparer.Ordinal);
        var include = new List<SelectorSourceRef>();
        foreach (var publisher in OcharoSourceRegistry.Current.Groups.OrderBy(group => group.Rank))
        {
            var group = groups.FirstOrDefault(item => string.Equals(item.Name, publisher.Name, StringComparison.OrdinalIgnoreCase));
            if (group is not null)
                include.Add(Bind(group, publisher, overrides.GetValueOrDefault(publisher.Id)));
        }
        return include.Count == 0
            ? Result<SubscriptionSelector>.Fail(AppErrorFactory.Validation(
                "No registry publisher is provisioned in the Ocharo container; run setup."))
            : Result<SubscriptionSelector>.Ok(Selector with { Sources = new SelectorSources { Include = include } });
    }

    private static SelectorSourceRef Bind(SourceGroupModel group, OcharoSourceGroup publisher, IReadOnlyDictionary<string, string>? context) =>
        new()
        {
            GroupId = group.Id,
            Context = new Dictionary<string, string>(context ?? new Dictionary<string, string>(), StringComparer.Ordinal)
            {
                ["sourceGroup"] = publisher.Id,
                ["sourceName"] = publisher.Name,
            },
        };

    private static OcharoSelector Load()
    {
        using Stream stream = typeof(OcharoSelector).Assembly.GetManifestResourceStream("Ocharo.Selector.json")
            ?? throw new InvalidOperationException("The Ocharo selector is missing.");
        var options = new JsonSerializerOptions(JsonSerializerDefaults.Web)
        {
            UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        };
        var selector = JsonSerializer.Deserialize<OcharoSelector>(stream, options)
            ?? throw new InvalidOperationException("The Ocharo selector is empty.");
        selector.Validate();
        return selector;
    }

    internal void Validate()
    {
        var groupIds = OcharoSourceRegistry.Current.Groups.Select(group => group.Id).ToHashSet(StringComparer.Ordinal);
        if (Format != FormatName || !SelectorRules.IsName(Name) || Schema != PostEnvelopeConstants.EventSchema
            || Selector.Sources.Include.Count > 0 || Selector.Sources.Exclude.Count > 0
            || (Sources.Count == 0 && !FollowRegistry) || Sources.Select(source => source.Group).Distinct(StringComparer.Ordinal).Count() != Sources.Count
            || Sources.Any(source => !groupIds.Contains(source.Group)))
            throw new InvalidOperationException("The Ocharo selector format, name, schema or publishers are invalid.");
        if (SelectorRules.Validate(Selector) is { } problem)
            throw new InvalidOperationException("The Ocharo selector is invalid: " + problem);
    }
}

/// <summary>One registry publisher a selector follows, and the defaults its events carry.</summary>
public sealed record OcharoSelectorSource
{
    /// <summary>Gets the registry's publisher id, such as <c>afisha</c>.</summary>
    public required string Group { get; init; }

    /// <summary>Gets the opaque defaults the publisher's events add to their payload context, such as
    /// <c>{"defaultKind":"trade-fair"}</c>.</summary>
    public IReadOnlyDictionary<string, string> Context { get; init; } = new Dictionary<string, string>();
}
