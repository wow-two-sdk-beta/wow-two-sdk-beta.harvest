using System.Data.Common;
using System.Text.Json;
using HarvestSources.Api.Configurations;
using Microsoft.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;

namespace HarvestSources.Api.Entities;

/// <summary>Operates the entity layer: resolves a post, queues stored answers, reads, searches, merges and splits entities
/// and reports.</summary>
/// <remarks>
///   - <c>resolve --post &lt;id&gt; [--dry-run]</c> binds a post's done extraction answers now; a dry run writes nothing
///   - <c>backfill [--since &lt;date&gt;] [--limit N]</c> queues posts whose answers have no relation or flag yet
///   - <c>show --id &lt;id&gt;</c> prints the entity, its occurrences by year and each relation's post, role and source
///   - <c>search --q &lt;text&gt; [--type event|listing] [--domain &lt;key&gt;] [--limit N]</c> prints entities whose name
///     or alias is similar, with their years
///   - <c>merge --from &lt;id&gt; --into &lt;id&gt;</c> and <c>split --a &lt;id&gt; --b &lt;id&gt;</c> correct entities
///   - <c>report</c> prints counts by type, cadence and status and the open unsure flags
///   - every sub-command prints one JSON line, except <c>show</c> and <c>search</c>, which print one JSON document; none
///     prints post text
/// </remarks>
public static class EntitiesVerb
{
    /// <summary>Holds the verb's name, the first process argument.</summary>
    public const string Name = "entities";

    private const string Usage = """
        Usage: entities resolve --post <id> [--dry-run]
               entities backfill [--since <date>] [--limit <n>]
               entities show --id <id>
               entities search --q <text> [--type <event|listing>] [--domain <key>] [--limit <n>]
               entities merge --from <id> --into <id>
               entities split --a <id> --b <id>
               entities report
        """;

    private static readonly JsonSerializerOptions Document = new(JsonOptionsConstants.Default) { WriteIndented = true };

    /// <summary>Runs one sub-command against the built host's services.</summary>
    /// <param name="services">The built host's services.</param>
    /// <param name="arguments">The arguments after the verb name.</param>
    /// <param name="output">Standard output for the JSON result.</param>
    /// <param name="error">Standard error for usage or a sanitized diagnostic.</param>
    /// <param name="ct">The token that cancels the verb.</param>
    /// <returns>Zero on success, one for a refusal, and two for invalid arguments.</returns>
    public static async Task<int> RunAsync(IServiceProvider services, IReadOnlyList<string> arguments, TextWriter output, TextWriter error,
        CancellationToken ct)
    {
        if (arguments.Count == 0 || !VerbArguments.TryOptions(arguments.Skip(1).ToArray(), out var options))
            return await UsageAsync(error);

        await using var scope = services.CreateAsyncScope();
        var entities = scope.ServiceProvider.GetService<IEntityBroker>();
        if (entities is null)
        {
            await error.WriteLineAsync("The entity layer is not composed in this host.");
            return 1;
        }
        try
        {
            return arguments[0] switch
            {
                "resolve" => await ResolveAsync(entities, options, output, error, ct),
                "backfill" => await BackfillAsync(entities, options, output, error, ct),
                "show" => await ShowAsync(entities, options, output, error, ct),
                "search" => await SearchAsync(entities, options, output, error, ct),
                "merge" => await MergeAsync(entities, options, output, error, ct),
                "split" => await SplitAsync(entities, options, output, error, ct),
                "report" => await ReportAsync(entities, options, output, error, ct),
                _ => await UsageAsync(error),
            };
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException)
        {
            await error.WriteLineAsync("The store could not be reached. Check the SCFD harvest migrations.");
            return 1;
        }
    }

    private static async Task<int> ResolveAsync(IEntityBroker entities, IReadOnlyDictionary<string, string?> options, TextWriter output,
        TextWriter error, CancellationToken ct)
    {
        if (!VerbArguments.Only(options, "post", "dry-run") || !TryId(options, "post", out var postId))
            return await UsageAsync(error);
        return await WriteAsync(await entities.ResolveAsync(postId, options.ContainsKey("dry-run"), ct), output, error);
    }

    private static async Task<int> BackfillAsync(IEntityBroker entities, IReadOnlyDictionary<string, string?> options, TextWriter output,
        TextWriter error, CancellationToken ct)
    {
        if (!VerbArguments.Only(options, "since", "limit") || !VerbArguments.TrySince(options, out var since)
            || !VerbArguments.TryInt(options, "limit", 1000, out var limit))
            return await UsageAsync(error);
        var result = await entities.BackfillAsync(new EntityBackfillRequest { SinceUtc = since, Limit = limit }, ct);
        if (result is not Result<int>.Success { Value: var queued })
            return await VerbArguments.RefusedAsync(error, result);
        await output.WriteLineAsync(JsonSerializer.Serialize(new { queued }));
        return 0;
    }

    private static async Task<int> ShowAsync(IEntityBroker entities, IReadOnlyDictionary<string, string?> options, TextWriter output,
        TextWriter error, CancellationToken ct)
    {
        if (!VerbArguments.Only(options, "id") || !TryId(options, "id", out var id))
            return await UsageAsync(error);
        return await WriteAsync(await entities.GetAsync(id, ct), output, error, Document);
    }

    private static async Task<int> SearchAsync(IEntityBroker entities, IReadOnlyDictionary<string, string?> options, TextWriter output,
        TextWriter error, CancellationToken ct)
    {
        if (!VerbArguments.Only(options, "q", "type", "domain", "limit") || options.GetValueOrDefault("q") is not { } query
            || !VerbArguments.TryInt(options, "limit", 20, out var limit))
            return await UsageAsync(error);
        return await WriteAsync(await entities.SearchAsync(new EntitySearchRequest
        {
            Query = query, EntityType = options.GetValueOrDefault("type"), Domain = options.GetValueOrDefault("domain"), Limit = limit,
        }, ct), output, error, Document);
    }

    private static async Task<int> MergeAsync(IEntityBroker entities, IReadOnlyDictionary<string, string?> options, TextWriter output,
        TextWriter error, CancellationToken ct)
    {
        if (!VerbArguments.Only(options, "from", "into") || !TryId(options, "from", out var from) || !TryId(options, "into", out var into))
            return await UsageAsync(error);
        return await WriteAsync(await entities.MergeAsync(from, into, ct), output, error);
    }

    private static async Task<int> SplitAsync(IEntityBroker entities, IReadOnlyDictionary<string, string?> options, TextWriter output,
        TextWriter error, CancellationToken ct)
    {
        if (!VerbArguments.Only(options, "a", "b") || !TryId(options, "a", out var left) || !TryId(options, "b", out var right))
            return await UsageAsync(error);
        return await WriteAsync(await entities.SplitAsync(left, right, ct), output, error);
    }

    private static async Task<int> ReportAsync(IEntityBroker entities, IReadOnlyDictionary<string, string?> options, TextWriter output,
        TextWriter error, CancellationToken ct)
    {
        if (options.Count > 0)
            return await UsageAsync(error);
        return await WriteAsync(await entities.ReportAsync(ct), output, error);
    }

    private static async Task<int> WriteAsync<T>(Result<T> result, TextWriter output, TextWriter error, JsonSerializerOptions? json = null)
        where T : notnull
    {
        if (result is not Result<T>.Success { Value: var value })
            return await VerbArguments.RefusedAsync(error, result);
        await output.WriteLineAsync(JsonSerializer.Serialize(value, json ?? JsonOptionsConstants.Default));
        return 0;
    }

    private static bool TryId(IReadOnlyDictionary<string, string?> options, string name, out Guid id) =>
        Guid.TryParseExact(options.GetValueOrDefault(name), "D", out id);

    private static async Task<int> UsageAsync(TextWriter error)
    {
        await error.WriteLineAsync(Usage);
        return 2;
    }
}
