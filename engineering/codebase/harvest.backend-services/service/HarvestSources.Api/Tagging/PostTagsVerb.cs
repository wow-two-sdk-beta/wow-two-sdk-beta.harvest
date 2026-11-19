using System.Data.Common;
using System.Text.Json;
using HarvestSources.Api.Configurations;
using Microsoft.EntityFrameworkCore;
using WoW.Two.Sdk.Beta.Be.Ai.Prompts;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;
using WoW.Two.Sdk.Beta.Be.HarvestAdapters.Ocharo;

namespace HarvestSources.Api.Tagging;

/// <summary>Operates the Layer 1 post-tags pass: registers its prompt, tags one post, queues stored posts and reports.</summary>
/// <remarks>
///   - <c>prompt --file &lt;draft.json | -&gt; [--by &lt;actor&gt;]</c> creates the versioned use case with the code-owned
///     schema (<c>KeepOutputs</c> true) and saves an immutable version; no prompt text lives in code
///   - <c>run --post &lt;id&gt; [--dry-run]</c> tags one post and prints the answer; a dry run writes nothing
///   - <c>backfill [--since &lt;date&gt;] [--limit N] [--source &lt;id&gt;]</c> queues stored posts without a done answer
///   - <c>report [--since &lt;date&gt;]</c> prints counts, flags, cost and p95 latency as one JSON line, no post text
///   - <c>relevance [--since &lt;date&gt;]</c> compares <c>ocharo-fashion-v1</c>'s verdict with the tags' verdict for
///     Ocharo's pair (<c>event</c> under <c>style-fashion</c>) over stored posts, as one JSON line of counts with invalid
///     answers, cost per tagged post and p95 latency; no post text
///   - <c>relevance --disagreements [--since &lt;date&gt;] [--limit N]</c> prints instead one JSON line listing the posts
///     whose rules verdict and tags verdict disagree (default 50): an operator review that carries each post's scrubbed,
///     clipped title; Wheelhouse's one-off command keeps a last line whole only up to 16 KiB, so a long list wants a lower
///     <c>--limit</c>
/// </remarks>
public static class PostTagsVerb
{
    /// <summary>Holds the verb's name, the first process argument.</summary>
    public const string Name = "post-tags";

    private const int DefaultDisagreements = 50;

    private const string Usage = """
        Usage: post-tags prompt --file <draft.json | -> [--by <actor>]
               post-tags run --post <id> [--dry-run]
               post-tags backfill [--since <date>] [--limit <n>] [--source <id>]
               post-tags report [--since <date>]
               post-tags relevance [--since <date>] [--disagreements [--limit <n>]]
        """;

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
        var tags = scope.ServiceProvider.GetService<IPostTagsBroker>();
        if (tags is null)
        {
            await error.WriteLineAsync($"Enable {RentalTermsConfiguration.EnabledSetting} on the internal source host.");
            return 1;
        }
        try
        {
            return arguments[0] switch
            {
                "prompt" => await PromptAsync(scope.ServiceProvider, options, input, output, error, ct),
                "run" => await RunPostAsync(tags, options, output, error, ct),
                "backfill" => await BackfillAsync(tags, options, output, error, ct),
                "report" => await ReportAsync(tags, options, output, error, ct),
                "relevance" => await RelevanceAsync(tags, options, output, error, ct),
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
        if (!VerbArguments.Only(options, "file", "by") || options.GetValueOrDefault("file") is not { } file)
            return await UsageAsync(error);
        string? by = options.GetValueOrDefault("by");
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
        if (draft is not { UseCase: PostTagsConstants.UseCase } || string.IsNullOrWhiteSpace(draft.SystemTemplate)
            || string.IsNullOrWhiteSpace(draft.UserTemplate))
        {
            await error.WriteLineAsync($"Provide system and user templates for {PostTagsConstants.UseCase}.");
            return 1;
        }
        if (BindingError(draft) is { } binding)
        {
            await error.WriteLineAsync(binding);
            return 1;
        }

        if (!await VerbArguments.EnsureUseCaseAsync(prompts, PostTagsConstants.UseCase,
                "Tag every SCFD post: entity type, IAB domains, English topics and routing fields.", PostTagsDefinition.OutputSchema, ct))
        {
            await error.WriteLineAsync("The post-tags use case requires the versioned SCFD prompt contract.");
            return 1;
        }
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

    private static async Task<int> RunPostAsync(IPostTagsBroker tags, IReadOnlyDictionary<string, string?> options, TextWriter output,
        TextWriter error, CancellationToken ct)
    {
        if (!VerbArguments.Only(options, "post", "dry-run") || !Guid.TryParseExact(options.GetValueOrDefault("post"), "D", out var postId))
            return await UsageAsync(error);
        var result = await tags.RunAsync(postId, options.ContainsKey("dry-run"), ct);
        if (result is not Result<PostTagsRunModel>.Success { Value: var run })
            return await VerbArguments.RefusedAsync(error, result);
        await output.WriteLineAsync(JsonSerializer.Serialize(run, JsonOptionsConstants.Default));
        return run.Status == "done" ? 0 : 1;
    }

    private static async Task<int> BackfillAsync(IPostTagsBroker tags, IReadOnlyDictionary<string, string?> options, TextWriter output,
        TextWriter error, CancellationToken ct)
    {
        if (!VerbArguments.Only(options, "since", "limit", "source") || !VerbArguments.TrySince(options, out var since)
            || !VerbArguments.TryInt(options, "limit", 1000, out var limit))
            return await UsageAsync(error);
        Guid? source = null;
        if (options.TryGetValue("source", out var raw))
        {
            if (!Guid.TryParseExact(raw, "D", out var parsed))
                return await UsageAsync(error);
            source = parsed;
        }
        var result = await tags.BackfillAsync(new PostTagsBackfillRequest { SinceUtc = since, Limit = limit, SourceId = source }, ct);
        if (result is not Result<int>.Success { Value: var queued })
            return await VerbArguments.RefusedAsync(error, result);
        await output.WriteLineAsync(JsonSerializer.Serialize(new { queued }));
        return 0;
    }

    private static async Task<int> ReportAsync(IPostTagsBroker tags, IReadOnlyDictionary<string, string?> options, TextWriter output,
        TextWriter error, CancellationToken ct)
    {
        if (!VerbArguments.Only(options, "since") || !VerbArguments.TrySince(options, out var since))
            return await UsageAsync(error);
        var result = await tags.ReportAsync(since, ct);
        if (result is not Result<PostTagsReportModel>.Success { Value: var report })
            return await VerbArguments.RefusedAsync(error, result);
        await output.WriteLineAsync(JsonSerializer.Serialize(report, JsonOptionsConstants.Default));
        return 0;
    }

    private static async Task<int> RelevanceAsync(IPostTagsBroker tags, IReadOnlyDictionary<string, string?> options, TextWriter output,
        TextWriter error, CancellationToken ct)
    {
        bool disagreements = options.ContainsKey("disagreements");
        if (!VerbArguments.Only(options, "since", "disagreements", "limit") || !VerbArguments.TrySince(options, out var since)
            || (!disagreements && options.ContainsKey("limit")) || !VerbArguments.TryInt(options, "limit", DefaultDisagreements, out var limit))
            return await UsageAsync(error);
        var request = new PostTagsRelevanceRequest
        {
            RulesFilterKey = OcharoFashionFilter.FilterKey, Wanted = [OcharoRelevanceFilter.Wanted], SinceUtc = since,
        };
        if (disagreements)
        {
            var listed = await tags.DisagreementsAsync(request, limit, ct);
            if (listed is not Result<PostTagsDisagreementsModel>.Success { Value: var review })
                return await VerbArguments.RefusedAsync(error, listed);
            await output.WriteLineAsync(JsonSerializer.Serialize(review, JsonOptionsConstants.Default));
            return 0;
        }
        var result = await tags.RelevanceAsync(request, ct);
        if (result is not Result<PostTagsRelevanceModel>.Success { Value: var report })
            return await VerbArguments.RefusedAsync(error, result);
        await output.WriteLineAsync(JsonSerializer.Serialize(report, JsonOptionsConstants.Default));
        return 0;
    }

    /// <summary>Names a template placeholder the pass never supplies, or a required one that is missing.</summary>
    private static string? BindingError(PromptDraft draft)
    {
        if (VerbArguments.UnknownPlaceholder(draft, PostTagsDefinition.Variables) is not null)
            return $"Use only the post-tags variables: {string.Join(", ", PostTagsDefinition.Variables)}.";
        if (!VerbArguments.Binds(draft.SystemTemplate, "taxonomy"))
            return "Include the {{taxonomy}} variable in systemTemplate.";
        return VerbArguments.Binds(draft.UserTemplate, "text") ? null : "Include the {{text}} variable in userTemplate.";
    }

    private static async Task<int> UsageAsync(TextWriter error)
    {
        await error.WriteLineAsync(Usage);
        return 2;
    }
}
