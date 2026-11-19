using System.Data.Common;
using System.Text.Json;
using HarvestSources.Api.Configurations;
using Microsoft.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Reprocessing;

namespace HarvestSources.Api.Processing;

/// <summary>Operates the AI passes from the host: re-runs, their progress, and eval sets from stored posts — one sub-command
/// per <c>api/processing</c> route.</summary>
/// <remarks>
///   - <c>rerun (--posts a,b | --sources a,b | --all-sources) [--use-case post-tags,extract-event,extract-listing]
///     [--since &lt;date&gt;] [--prompt-before &lt;version&gt;] [--dry-run] [--by &lt;actor&gt;]</c> mirrors
///     <c>POST api/processing/reruns</c>
///   - <c>status --run &lt;id&gt;</c> mirrors <c>GET api/processing/reruns/{id}</c>
///   - <c>eval-set --set &lt;key&gt; --use-case &lt;use case&gt; [--sources a,b] [--sample N] [--seed N]
///     [--expected empty|current] [--since &lt;date&gt;] [--score-fields a,b] [--note &lt;text&gt;]</c> mirrors
///     <c>POST api/processing/eval-sets</c>
///   - each prints one JSON line and no post text
/// </remarks>
public static class ProcessingVerb
{
    /// <summary>Holds the verb's name, the first process argument.</summary>
    public const string Name = "processing";

    private const string Usage = """
        Usage: processing rerun (--posts <id,id> | --sources <id,id> | --all-sources) [--use-case <post-tags,extract-event,extract-listing>]
                                [--since <date>] [--prompt-before <version>] [--dry-run] [--by <actor>]
               processing status --run <id>
               processing eval-set --set <key> --use-case <use case> [--sources <id,id>] [--sample <n>] [--seed <n>]
                                   [--expected empty|current] [--since <date>] [--score-fields <a,b>] [--note <text>]
        """;

    /// <summary>Runs one sub-command against the built internal host's services.</summary>
    /// <param name="services">The built host's services.</param>
    /// <param name="arguments">The arguments after the verb name.</param>
    /// <param name="output">Standard output for one JSON line.</param>
    /// <param name="error">Standard error for usage or a sanitized diagnostic.</param>
    /// <param name="ct">The token that cancels the verb.</param>
    /// <returns>Zero on success, one for a refusal, and two for invalid arguments.</returns>
    public static async Task<int> RunAsync(IServiceProvider services, IReadOnlyList<string> arguments, TextWriter output, TextWriter error,
        CancellationToken ct)
    {
        if (arguments.Count == 0 || !VerbArguments.TryOptions(arguments.Skip(1).ToArray(), out var options))
            return await UsageAsync(error);

        await using var scope = services.CreateAsyncScope();
        var reprocessing = scope.ServiceProvider.GetRequiredService<IPostReprocessingBroker>();
        try
        {
            return arguments[0] switch
            {
                "rerun" => await RerunAsync(reprocessing, options, output, error, ct),
                "status" => await StatusAsync(reprocessing, options, output, error, ct),
                "eval-set" => await EvalSetAsync(reprocessing, options, output, error, ct),
                _ => await UsageAsync(error),
            };
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException)
        {
            await error.WriteLineAsync("The store could not be reached. Check the SCFD harvest and ai migrations.");
            return 1;
        }
    }

    private static async Task<int> RerunAsync(IPostReprocessingBroker reprocessing, IReadOnlyDictionary<string, string?> options,
        TextWriter output, TextWriter error, CancellationToken ct)
    {
        if (!VerbArguments.Only(options, "posts", "sources", "all-sources", "use-case", "since", "prompt-before", "dry-run", "by")
            || !VerbArguments.TryIds(options, "posts", out var posts) || !VerbArguments.TryIds(options, "sources", out var sources)
            || !VerbArguments.TrySince(options, out var since) || !TryOptionalInt(options, "prompt-before", out var before))
            return await UsageAsync(error);
        var result = await reprocessing.RerunAsync(new PostRerunRequest
        {
            PostIds = posts, SourceIds = sources, AllSources = options.ContainsKey("all-sources"),
            UseCases = VerbArguments.List(options, "use-case"), SinceUtc = since, PromptBefore = before,
            DryRun = options.ContainsKey("dry-run"), RequestedBy = options.GetValueOrDefault("by") ?? "verb",
        }, ct);
        if (result is not Result<PostRerunModel>.Success { Value: var run })
            return await VerbArguments.RefusedAsync(error, result);
        await output.WriteLineAsync(JsonSerializer.Serialize(run, JsonOptionsConstants.Default));
        return 0;
    }

    private static async Task<int> StatusAsync(IPostReprocessingBroker reprocessing, IReadOnlyDictionary<string, string?> options,
        TextWriter output, TextWriter error, CancellationToken ct)
    {
        if (!VerbArguments.Only(options, "run") || !Guid.TryParseExact(options.GetValueOrDefault("run"), "D", out var runId))
            return await UsageAsync(error);
        var result = await reprocessing.GetRunAsync(runId, ct);
        if (result is not Result<PostRerunStatusModel>.Success { Value: var status })
            return await VerbArguments.RefusedAsync(error, result);
        await output.WriteLineAsync(JsonSerializer.Serialize(status, JsonOptionsConstants.Default));
        return 0;
    }

    private static async Task<int> EvalSetAsync(IPostReprocessingBroker reprocessing, IReadOnlyDictionary<string, string?> options,
        TextWriter output, TextWriter error, CancellationToken ct)
    {
        if (!VerbArguments.Only(options, "set", "use-case", "sources", "sample", "seed", "expected", "since", "score-fields", "note")
            || options.GetValueOrDefault("set") is not { } set || options.GetValueOrDefault("use-case") is not { } useCase
            || !VerbArguments.TryIds(options, "sources", out var sources) || !VerbArguments.TryInt(options, "sample", 50, out var sample)
            || !VerbArguments.TryInt(options, "seed", 1, out var seed) || !VerbArguments.TrySince(options, out var since))
            return await UsageAsync(error);
        var result = await reprocessing.BuildEvalSetAsync(new PostEvalSetRequest
        {
            Set = set, UseCase = useCase, SourceIds = sources, Sample = sample, Seed = seed, SinceUtc = since,
            Expected = options.GetValueOrDefault("expected") ?? PostReprocessingConstants.ExpectedEmpty,
            ScoreFields = VerbArguments.List(options, "score-fields"), Note = options.GetValueOrDefault("note"),
        }, ct);
        if (result is not Result<PostEvalSetModel>.Success { Value: var built })
            return await VerbArguments.RefusedAsync(error, result);
        await output.WriteLineAsync(JsonSerializer.Serialize(built, JsonOptionsConstants.Default));
        return 0;
    }

    private static bool TryOptionalInt(IReadOnlyDictionary<string, string?> options, string name, out int? value)
    {
        value = null;
        if (!options.ContainsKey(name))
            return true;
        if (!VerbArguments.TryInt(options, name, 0, out var parsed))
            return false;
        value = parsed;
        return true;
    }

    private static async Task<int> UsageAsync(TextWriter error)
    {
        await error.WriteLineAsync(Usage);
        return 2;
    }
}
