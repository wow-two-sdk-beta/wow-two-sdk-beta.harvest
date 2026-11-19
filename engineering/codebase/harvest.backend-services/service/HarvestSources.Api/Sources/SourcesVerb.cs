using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace HarvestSources.Api.Sources;

/// <summary>Answers <c>sources processing</c>: reads and sets each source's model-processing mode from the deployed
/// host, so a platform job switches sources with no operator token — the verb form of <c>PUT api/sources/{id}</c>
/// <c>{ "modelProcessing": … }</c>.</summary>
/// <remarks>
///   - <c>sources processing (--source &lt;id|family/handle&gt;... | --group &lt;name&gt;...) --mode off|manual|auto [--tenant &lt;slug&gt;]</c>
///     sets the mode of every source named; a source is an id or <c>family/handle</c> (<c>site/afisha.uz</c>), a group is
///     a source group's name without regard to case, and a source named by both is set once
///   - <c>sources processing --list [--source …]... [--group …]... [--tenant &lt;slug&gt;]</c> writes nothing; with no
///     <c>--source</c> or <c>--group</c> it lists every source
///   - <c>--tenant</c> narrows every selector and the listing to one tenant's containers; a <c>family/handle</c> that
///     matches sources of several tenants is refused, and nothing is changed until every selector has resolved
///   - stdout carries one line per source: <c>&lt;id&gt; &lt;family/handle&gt; &lt;old&gt; -&gt; &lt;new&gt;</c> when setting
///     (an unchanged source reads <c>auto -&gt; auto</c> and is not written), <c>&lt;id&gt; &lt;family/handle&gt; &lt;mode&gt;</c>
///     when listing; stderr carries the refusals and a count
///   - exit codes: <c>0</c> done, <c>1</c> refused or nothing selected, <c>2</c> usage
/// </remarks>
public static class SourcesVerb
{
    /// <summary>Holds the verb's name, the first process argument.</summary>
    public const string Name = "sources";

    private const string Usage = """
        Usage: sources processing (--source <id|family/handle>... | --group <name>...) --mode <off|manual|auto> [--tenant <slug>]
               sources processing --list [--source <id|family/handle>...] [--group <name>...] [--tenant <slug>]
        """;

    /// <summary>Runs the verb over the host's services.</summary>
    /// <param name="services">The built host's services.</param>
    /// <param name="arguments">The arguments after the verb's name.</param>
    /// <param name="output">Standard output: one line per source.</param>
    /// <param name="error">Standard error: refusals and a count.</param>
    /// <param name="ct">The token that cancels the verb.</param>
    /// <returns>The exit code.</returns>
    public static async Task<int> RunAsync(IServiceProvider services, IReadOnlyList<string> arguments, TextWriter output,
        TextWriter error, CancellationToken ct)
    {
        if (!TryParse(arguments, out var request))
        {
            await error.WriteLineAsync(Usage);
            return 2;
        }

        await using var scope = services.CreateAsyncScope();
        if (scope.ServiceProvider.GetService<IRegistrationBroker>() is not { } registry)
        {
            await error.WriteLineAsync("sources processing runs on the internal source host; HarvestSources:Audience is public.");
            return 1;
        }

        try
        {
            var all = await ReadAsync(registry, request.Tenant, ct);
            var selected = Select(all, request);
            if (selected.Count == 0)
                throw new Refusal("No source is selected.");

            if (request.List)
            {
                foreach (var entry in selected)
                    await output.WriteLineAsync($"{entry.Source.Id:D} {entry.Label} {Format(entry.Source.ModelProcessing)}");
                return 0;
            }

            var mode = request.Mode!.Value;
            int changed = 0;
            foreach (var entry in selected)
            {
                var old = entry.Source.ModelProcessing;
                if (old != mode)
                {
                    Value(await registry.UpdateSourceAsync(entry.Source.Id, new() { ModelProcessing = mode }, ct));
                    changed++;
                }
                await output.WriteLineAsync($"{entry.Source.Id:D} {entry.Label} {Format(old)} -> {Format(mode)}");
            }
            await error.WriteLineAsync($"{changed} of {selected.Count} source(s) changed to {Format(mode)}");
            return 0;
        }
        catch (Refusal refusal)
        {
            await error.WriteLineAsync("sources processing: " + refusal.Message);
            return 1;
        }
    }

    /// <summary>Reads a mode as the verbs spell it: <c>off</c>, <c>manual</c> or <c>auto</c>, in any case.</summary>
    /// <param name="value">The argument.</param>
    /// <param name="mode">The mode when the argument names one.</param>
    /// <returns>Whether the argument names a mode.</returns>
    internal static bool TryParseMode(string? value, out ModelProcessingMode mode)
    {
        foreach (var candidate in Enum.GetValues<ModelProcessingMode>())
        {
            if (string.Equals(Format(candidate), value, StringComparison.OrdinalIgnoreCase))
            {
                mode = candidate;
                return true;
            }
        }
        mode = default;
        return false;
    }

    private static string Format(ModelProcessingMode mode) => mode.ToString().ToLowerInvariant();

    private static string Label(SourceModel source) =>
        JsonNamingPolicy.SnakeCaseLower.ConvertName(source.Family.ToString()) + "/" + source.Handle;

    private static async Task<List<Entry>> ReadAsync(IRegistrationBroker registry, string? tenantSlug, CancellationToken ct)
    {
        Guid? tenantId = null;
        if (tenantSlug is not null)
        {
            var tenant = Value(await registry.ListTenantsAsync(ct)).SingleOrDefault(item => item.Slug == tenantSlug)
                ?? throw new Refusal($"No tenant '{tenantSlug}'.");
            tenantId = tenant.Id;
        }

        var entries = new List<Entry>();
        foreach (var container in Value(await registry.ListContainersAsync(tenantId, ct)))
        {
            var groups = Value(await registry.ListSourceGroupsAsync(container.Id, ct)).ToDictionary(group => group.Id, group => group.Name);
            foreach (var source in Value(await registry.ListSourcesAsync(container.Id, ct)))
                entries.Add(new(source, Label(source), source.GroupId is { } id && groups.TryGetValue(id, out var name) ? name : null));
        }
        return entries.OrderBy(entry => entry.Label, StringComparer.Ordinal).ThenBy(entry => entry.Source.Id).ToList();
    }

    private static List<Entry> Select(List<Entry> all, Request request)
    {
        if (request.Sources.Count == 0 && request.Groups.Count == 0)
            return all;

        var chosen = new HashSet<Guid>();
        foreach (var token in request.Sources)
        {
            var matches = (Guid.TryParseExact(token, "D", out var id)
                ? all.Where(entry => entry.Source.Id == id)
                : all.Where(entry => string.Equals(entry.Label, token, StringComparison.OrdinalIgnoreCase))).ToList();
            if (matches.Count == 0)
                throw new Refusal($"No source matches '{token}'.");
            if (matches.Count > 1)
                throw new Refusal($"'{token}' matches {matches.Count} sources ({string.Join(", ", matches.Select(entry => entry.Source.Id.ToString("D")))}); "
                    + "name one by its id or add --tenant.");
            chosen.Add(matches[0].Source.Id);
        }
        foreach (var name in request.Groups)
        {
            var matches = all.Where(entry => string.Equals(entry.GroupName, name, StringComparison.OrdinalIgnoreCase)).ToList();
            if (matches.Count == 0)
                throw new Refusal($"No source group named '{name}' holds a source.");
            chosen.UnionWith(matches.Select(entry => entry.Source.Id));
        }
        return all.Where(entry => chosen.Contains(entry.Source.Id)).ToList();
    }

    private static bool TryParse(IReadOnlyList<string> arguments, out Request request)
    {
        request = new();
        if (arguments is not ["processing", ..])
            return false;
        for (var index = 1; index < arguments.Count; index++)
        {
            string? next = index + 1 < arguments.Count && !arguments[index + 1].StartsWith("--", StringComparison.Ordinal)
                && !string.IsNullOrWhiteSpace(arguments[index + 1]) ? arguments[index + 1] : null;
            switch (arguments[index])
            {
                case "--list" when !request.List:
                    request.List = true;
                    break;
                case "--source" when next is not null:
                    request.Sources.Add(next);
                    index++;
                    break;
                case "--group" when next is not null:
                    request.Groups.Add(next);
                    index++;
                    break;
                case "--tenant" when next is not null && request.Tenant is null:
                    request.Tenant = next;
                    index++;
                    break;
                case "--mode" when next is not null && request.Mode is null && TryParseMode(next, out var mode):
                    request.Mode = mode;
                    index++;
                    break;
                default:
                    return false;
            }
        }
        return request.List
            ? request.Mode is null
            : request.Mode is not null && (request.Sources.Count > 0 || request.Groups.Count > 0);
    }

    private static T Value<T>(Result<T> result) where T : notnull => result switch
    {
        Result<T>.Success success => success.Value,
        Result<T>.Failure failure => throw new Refusal(failure.Error.Message),
        _ => throw new InvalidOperationException("Unexpected registry result."),
    };

    private sealed class Request
    {
        internal bool List { get; set; }
        internal List<string> Sources { get; } = [];
        internal List<string> Groups { get; } = [];
        internal string? Tenant { get; set; }
        internal ModelProcessingMode? Mode { get; set; }
    }

    private sealed record Entry(SourceModel Source, string Label, string? GroupName);

    private sealed class Refusal(string message) : Exception(message);
}
