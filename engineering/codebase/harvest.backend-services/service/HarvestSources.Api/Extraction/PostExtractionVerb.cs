using System.Data.Common;
using System.Text.Json;
using HarvestSources.Api.Configurations;
using Microsoft.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Ai.Prompts;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;

namespace HarvestSources.Api.Extraction;

/// <summary>Operates Layer 2 extraction: registers each use case's prompt, extracts one post, queues stored posts for the
/// wanted pairs and reports.</summary>
/// <remarks>
///   - <c>prompt --use-case &lt;event|listing&gt; --file &lt;draft.json | -&gt; [--by &lt;actor&gt;]</c> creates the
///     versioned <c>extract-event</c> or <c>extract-listing</c> use case with the code-owned schema (<c>KeepOutputs</c>
///     true) and saves an immutable version; an existing use case with an older code schema takes the new one in place; no
///     prompt text lives in code; the listing draft may also use the <c>attributes</c>, <c>section</c> and <c>seller</c> variables
///   - <c>run --post &lt;id&gt; [--dry-run]</c> extracts one tagged post and prints the checked answer; a dry run stores nothing
///   - <c>backfill [--since &lt;date&gt;] [--limit N]</c> queues stored posts whose tags match a wanted pair
///   - <c>report [--since &lt;date&gt;]</c> prints counts per use case by status and role, flags, cost and p95 latency as
///     one JSON line, no post text
/// </remarks>
public static class PostExtractionVerb
{
    /// <summary>Holds the verb's name, the first process argument.</summary>
    public const string Name = "extract";

    private const string Usage = """
        Usage: extract prompt --use-case <event|listing> --file <draft.json | -> [--by <actor>]
               extract run --post <id> [--dry-run]
               extract backfill [--since <date>] [--limit <n>]
               extract report [--since <date>]
        """;

    private static readonly IReadOnlyDictionary<string, (string Key, string Description)> UseCases = new Dictionary<string, (string, string)>
    {
        ["event"] = (PostExtractionConstants.EventUseCase,
            "Parse an SCFD event post: name, occurrences, place, format, access, prices, registration, relation and a fashion block."),
        ["listing"] = (PostExtractionConstants.ListingUseCase,
            "Parse an SCFD listing post (schema 2): property kind, offer, price, the building and unit, amenities, location, landmarks, author kind, relation, rental terms with evidence, tenants and urgency."),
    };

    /// <summary>Runs one sub-command against the built internal host's services.</summary>
    /// <param name="services">The built host's services.</param>
    /// <param name="arguments">The arguments after the verb name.</param>
    /// <param name="input">Standard input, read when the prompt file is <c>-</c>.</param>
    /// <param name="output">Standard output for one JSON line.</param>
    /// <param name="error">Standard error for usage or a sanitized diagnostic.</param>
    /// <param name="ct">The token that cancels the verb.</param>
    /// <returns>Zero on success, one for a refusal or a failed answer, and two for invalid arguments.</returns>
    public static async Task<int> RunAsync(IServiceProvider services, IReadOnlyList<string> arguments, Stream input,
        TextWriter output, TextWriter error, CancellationToken ct)
    {
        if (arguments.Count == 0 || !VerbArguments.TryOptions(arguments.Skip(1).ToArray(), out var options))
            return await UsageAsync(error);

        await using var scope = services.CreateAsyncScope();
        var extraction = scope.ServiceProvider.GetService<IPostExtractionBroker>();
        if (extraction is null)
        {
            await error.WriteLineAsync($"Enable {RentalTermsConfiguration.EnabledSetting} on the internal source host.");
            return 1;
        }
        try
        {
            return arguments[0] switch
            {
                "prompt" => await PromptAsync(scope.ServiceProvider, options, input, output, error, ct),
                "run" => await RunPostAsync(extraction, options, output, error, ct),
                "backfill" => await BackfillAsync(extraction, options, output, error, ct),
                "report" => await ReportAsync(extraction, options, output, error, ct),
                _ => await UsageAsync(error),
            };
        }
        catch (Exception exception) when (exception is DbException or DbUpdateException)
        {
            await error.WriteLineAsync("The store could not be reached. Check the SCFD harvest and ai migrations.");
            return 1;
        }
    }

    private static async Task<int> PromptAsync(IServiceProvider services, IReadOnlyDictionary<string, string?> options, Stream input,
        TextWriter output, TextWriter error, CancellationToken ct)
    {
        if (!VerbArguments.Only(options, "use-case", "file", "by") || options.GetValueOrDefault("file") is not { } file
            || !UseCases.TryGetValue(options.GetValueOrDefault("use-case") ?? "", out var useCase))
            return await UsageAsync(error);
        if (services.GetService<IPromptsBroker>() is not { } prompts)
        {
            await error.WriteLineAsync($"Enable {RentalTermsConfiguration.EnabledSetting} on the internal source host.");
            return 1;
        }

        var (draft, unreadable) = await VerbArguments.ReadDraftAsync(file, input, ct);
        if (unreadable is not null)
        {
            await error.WriteLineAsync(unreadable);
            return 1;
        }
        if (draft is null || draft.UseCase != useCase.Key || string.IsNullOrWhiteSpace(draft.SystemTemplate)
            || string.IsNullOrWhiteSpace(draft.UserTemplate))
        {
            await error.WriteLineAsync($"Provide system and user templates for {useCase.Key}.");
            return 1;
        }
        var variables = PostExtractionDefinitions.VariablesOf(useCase.Key);
        if (VerbArguments.UnknownPlaceholder(draft, variables) is not null)
        {
            await error.WriteLineAsync($"Use only the {useCase.Key} variables: {string.Join(", ", variables)}.");
            return 1;
        }
        if (!VerbArguments.Binds(draft.UserTemplate, "text"))
        {
            await error.WriteLineAsync("Include the {{text}} variable in userTemplate.");
            return 1;
        }

        if (!await VerbArguments.EnsureUseCaseAsync(prompts, useCase.Key, useCase.Description,
                PostExtractionDefinitions.OutputSchema(useCase.Key)!, ct, refreshSchema: true))
        {
            await error.WriteLineAsync($"The {useCase.Key} use case requires the versioned SCFD prompt contract.");
            return 1;
        }
        var by = options.GetValueOrDefault("by");
        var result = await prompts.SavePromptAsync(draft with { By = by?.Trim() ?? draft.By }, ct);
        if (result is not Result<PromptModel>.Success { Value: var saved })
        {
            await error.WriteLineAsync("The prompt was refused. Check the model match and prompt parameters.");
            return 1;
        }
        await output.WriteLineAsync(JsonSerializer.Serialize(new
        {
            useCase = saved.UseCase, version = saved.Version, contentHash = saved.ContentHash, id = saved.Id,
        }));
        return 0;
    }

    private static async Task<int> RunPostAsync(IPostExtractionBroker extraction, IReadOnlyDictionary<string, string?> options,
        TextWriter output, TextWriter error, CancellationToken ct)
    {
        if (!VerbArguments.Only(options, "post", "dry-run") || !Guid.TryParseExact(options.GetValueOrDefault("post"), "D", out var postId))
            return await UsageAsync(error);
        var result = await extraction.RunAsync(postId, options.ContainsKey("dry-run"), ct);
        if (result is not Result<PostExtractionRunModel>.Success { Value: var run })
            return await VerbArguments.RefusedAsync(error, result);
        await output.WriteLineAsync(JsonSerializer.Serialize(run, JsonOptionsConstants.Default));
        return run.Status == "done" ? 0 : 1;
    }

    private static async Task<int> BackfillAsync(IPostExtractionBroker extraction, IReadOnlyDictionary<string, string?> options,
        TextWriter output, TextWriter error, CancellationToken ct)
    {
        if (!VerbArguments.Only(options, "since", "limit") || !VerbArguments.TrySince(options, out var since)
            || !VerbArguments.TryInt(options, "limit", 1000, out var limit))
            return await UsageAsync(error);
        var result = await extraction.BackfillAsync(new PostExtractionBackfillRequest { SinceUtc = since, Limit = limit }, ct);
        if (result is not Result<int>.Success { Value: var queued })
            return await VerbArguments.RefusedAsync(error, result);
        await output.WriteLineAsync(JsonSerializer.Serialize(new { queued }));
        return 0;
    }

    private static async Task<int> ReportAsync(IPostExtractionBroker extraction, IReadOnlyDictionary<string, string?> options,
        TextWriter output, TextWriter error, CancellationToken ct)
    {
        if (!VerbArguments.Only(options, "since") || !VerbArguments.TrySince(options, out var since))
            return await UsageAsync(error);
        var result = await extraction.ReportAsync(since, ct);
        if (result is not Result<PostExtractionReportModel>.Success { Value: var report })
            return await VerbArguments.RefusedAsync(error, result);
        await output.WriteLineAsync(JsonSerializer.Serialize(report, JsonOptionsConstants.Default));
        return 0;
    }

    private static async Task<int> UsageAsync(TextWriter error)
    {
        await error.WriteLineAsync(Usage);
        return 2;
    }
}
