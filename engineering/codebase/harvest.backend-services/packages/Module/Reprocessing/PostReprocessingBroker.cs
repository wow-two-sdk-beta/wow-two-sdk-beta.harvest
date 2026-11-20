using System.Text.RegularExpressions;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Ai.Evals;
using WoW.Two.Sdk.Beta.Be.Ai.Inferences;
using WoW.Two.Sdk.Beta.Be.Ai.Runs;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Budget;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.PostExtraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Tagging;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Reprocessing;

/// <summary>Queues operator re-runs through the passes' work queue, reads their progress, and fills eval sets from stored posts.</summary>
internal sealed partial class PostReprocessingBroker(
    [FromKeyedServices(HarvestModuleConstants.ServiceKey)] NpgsqlDataSource dataSource,
    PostTagsGate tagging,
    PostExtractionGate extraction,
    IOptions<PostTagsOptions> tagOptions,
    IOptions<PostExtractionOptions> extractionOptions,
    IProcessingBudget budget,
    IServiceProvider services,
    TimeProvider time) : IPostReprocessingBroker
{
    private const int CostSample = 500;
    private static readonly TimeSpan CostWindow = TimeSpan.FromDays(30);

    /// <inheritdoc />
    public async Task<Result<PostRerunModel>> RerunAsync(PostRerunRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (services.GetService<IAiRunner>() is null)
            return Result<PostRerunModel>.Fail(AppErrorFactory.Conflict("The shared AI runner is not composed in this host."));
        if (Validate(request) is { } invalid)
            return Result<PostRerunModel>.Fail(invalid);
        var useCases = request.UseCases.Count == 0 ? PostReprocessingConstants.UseCases
            : PostReprocessingConstants.UseCases.Where(request.UseCases.Contains).ToArray();
        if (useCases.Contains(PostTagsConstants.UseCase) && !tagging.Enabled)
            return Result<PostRerunModel>.Fail(AppErrorFactory.Conflict(
                $"Tagging is off in this host: set {PostTagsConstants.SectionName}:Enabled before re-running post-tags."));

        var scope = new RerunScope(request.PostIds.Count > 0 ? [.. request.PostIds.Distinct()] : null,
            request.SourceIds.Count > 0 ? [.. request.SourceIds.Distinct()] : null, request.SinceUtc?.ToUniversalTime(), request.PromptBefore);
        var wanted = await extraction.WantedAsync(cancellationToken);
        var now = time.GetUtcNow();
        var runId = Guid.CreateVersion7(now);
        var counts = new SortedDictionary<string, PostRerunCounts>(StringComparer.Ordinal);

        await using (var connection = await dataSource.OpenConnectionAsync(cancellationToken))
        await using (var transaction = await connection.BeginTransactionAsync(cancellationToken))
        {
            string? by = string.IsNullOrWhiteSpace(request.RequestedBy) ? null : request.RequestedBy.Trim()[..Math.Min(100, request.RequestedBy.Trim().Length)];
            await ReprocessingStore.InsertRunAsync(connection, transaction, runId, useCases, scope, by, now, cancellationToken);
            // Tags first: an extraction of the same run leaves out the posts queued for tags, whose extraction follows them.
            foreach (var useCase in useCases)
                counts[useCase] = await ReprocessingStore.QueueAsync(connection, transaction, runId, scope, useCase, wanted, now, cancellationToken);
            await ReprocessingStore.SaveCountsAsync(connection, transaction, runId, counts, cancellationToken);
            // A dry run takes the same path and rolls it back, so its counts are exactly what a run would queue.
            if (request.DryRun)
                await transaction.RollbackAsync(cancellationToken);
            else
                await transaction.CommitAsync(cancellationToken);
        }

        foreach (var useCase in useCases)
            counts[useCase] = await EstimateAsync(useCase, counts[useCase], now, cancellationToken);
        var estimates = counts.Values.Select(count => count.EstimatedCostMicroUsd).OfType<long>().ToArray();
        return Result<PostRerunModel>.Ok(new PostRerunModel
        {
            RunId = request.DryRun ? null : runId, DryRun = request.DryRun, Scope = scope.Name, UseCases = useCases,
            Queued = counts.Values.Sum(count => count.Queued), Skipped = counts.Values.Sum(count => count.Skipped),
            Refused = counts.Values.Sum(count => count.Refused), EstimatedCostMicroUsd = estimates.Length > 0 ? estimates.Sum() : null,
            ByUseCase = counts, CreatedAtUtc = now, Budget = request.DryRun ? await budget.ReadAsync(cancellationToken) : null,
        });
    }

    /// <inheritdoc />
    public async Task<Result<PostRerunStatusModel>> GetRunAsync(Guid runId, CancellationToken cancellationToken = default)
    {
        if (await ReprocessingStore.ReadRunAsync(dataSource, runId, cancellationToken) is not { } run)
            return Result<PostRerunStatusModel>.Fail(AppErrorFactory.NotFound("The re-run was not found."));
        var remaining = await ReprocessingStore.ProgressAsync(dataSource, runId, time.GetUtcNow(), cancellationToken);
        return Result<PostRerunStatusModel>.Ok(new PostRerunStatusModel
        {
            RunId = runId, Scope = run.Scope, UseCases = run.UseCases, SinceUtc = run.Since, PromptBefore = run.PromptBefore,
            RequestedBy = run.By, CreatedAtUtc = run.At,
            Requested = StoreJson.Read(run.Counts, new SortedDictionary<string, PostRerunCounts>(StringComparer.Ordinal)),
            Remaining = remaining, Done = remaining.Count == 0,
        });
    }

    /// <inheritdoc />
    public async Task<Result<PostEvalSetModel>> BuildEvalSetAsync(PostEvalSetRequest request, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(request);
        if (services.GetService<IEvalsBroker>() is not { } evals)
            return Result<PostEvalSetModel>.Fail(AppErrorFactory.Conflict("The shared AI eval bench is not composed in this host."));
        if (string.IsNullOrWhiteSpace(request.Set) || !SetKey().IsMatch(request.Set))
            return Invalid<PostEvalSetModel>("A set key is lowercase letters, digits and hyphens.");
        if (!PostReprocessingConstants.UseCases.Contains(request.UseCase))
            return Invalid<PostEvalSetModel>($"The use case is one of {string.Join(", ", PostReprocessingConstants.UseCases)}.");
        if (request.Sample is < 1 or > PostReprocessingConstants.MaxSample)
            return Invalid<PostEvalSetModel>($"Sample 1 to {PostReprocessingConstants.MaxSample} posts.");
        if (request.SourceIds.Count > PostReprocessingConstants.MaxSources)
            return Invalid<PostEvalSetModel>($"List at most {PostReprocessingConstants.MaxSources} sources.");
        if (request.Expected is not (PostReprocessingConstants.ExpectedEmpty or PostReprocessingConstants.ExpectedCurrent))
            return Invalid<PostEvalSetModel>("Expected answers start empty or current.");

        bool created = false;
        switch (await evals.GetSetAsync(request.Set, cancellationToken))
        {
            case Result<EvalSetModel>.Success { Value: var existing } when existing.UseCase != request.UseCase:
                return Result<PostEvalSetModel>.Fail(AppErrorFactory.Conflict($"The set evaluates {existing.UseCase}, not {request.UseCase}."));
            case Result<EvalSetModel>.Success:
                break;
            case Result<EvalSetModel>.Failure { Error.Type: AppErrorType.NotFound }:
                if (await evals.PutSetAsync(new EvalSetModel
                    {
                        Key = request.Set, UseCase = request.UseCase, ScoreFields = request.ScoreFields,
                        Note = string.IsNullOrWhiteSpace(request.Note) ? "Sampled from stored SCFD posts." : request.Note.Trim(),
                    }, cancellationToken) is Result<EvalSetModel>.Failure { Error: var putError })
                    return Result<PostEvalSetModel>.Fail(putError);
                created = true;
                break;
            case Result<EvalSetModel>.Failure { Error: var error }:
                return Result<PostEvalSetModel>.Fail(error);
        }

        bool current = request.Expected == PostReprocessingConstants.ExpectedCurrent;
        var sampled = await ReprocessingStore.SampleAsync(dataSource, request.UseCase, [.. request.SourceIds.Distinct()],
            request.SinceUtc?.ToUniversalTime(), request.Sample, request.Seed, current, cancellationToken);
        var present = created ? new HashSet<string>(StringComparer.Ordinal)
            : (await evals.ListItemsAsync(request.Set, cancellationToken)).Match(
                items => items.Select(item => item.Subject).OfType<string>().ToHashSet(StringComparer.Ordinal),
                _ => new HashSet<string>(StringComparer.Ordinal));
        var added = new List<Guid>();
        foreach (var post in sampled)
        {
            string subject = $"{PostTagsConstants.SubjectPrefix}{post.PostId:D}@{post.Revision}";
            if (!present.Add(subject))
                continue;
            var item = await evals.AddItemAsync(new EvalItemDraft
            {
                Set = request.Set, Subject = subject, Expected = current ? post.Current ?? "{}" : "{}",
                Tags = [PostReprocessingConstants.UnreviewedTag, current ? "expected:current" : "expected:empty"],
                LabelledBy = current ? PostReprocessingConstants.CurrentAnswerLabel : null,
            }, cancellationToken);
            if (item is Result<EvalItemModel>.Success { Value: var model })
                added.Add(model.Id);
        }
        return Result<PostEvalSetModel>.Ok(new PostEvalSetModel
        {
            Set = request.Set, UseCase = request.UseCase, Created = created, Sampled = sampled.Count, Added = added.Count,
            Skipped = sampled.Count - added.Count, ItemIds = added,
        });
    }

    private static AppError? Validate(PostRerunRequest request)
    {
        int scopes = (request.PostIds.Count > 0 ? 1 : 0) + (request.SourceIds.Count > 0 ? 1 : 0) + (request.AllSources ? 1 : 0);
        if (scopes != 1)
            return AppErrorFactory.Validation("Name posts, list sources or pick all sources: exactly one scope.");
        if (request.PostIds.Count > PostReprocessingConstants.MaxPosts)
            return AppErrorFactory.Validation($"Name at most {PostReprocessingConstants.MaxPosts} posts.");
        if (request.SourceIds.Count > PostReprocessingConstants.MaxSources)
            return AppErrorFactory.Validation($"List at most {PostReprocessingConstants.MaxSources} sources.");
        if (request.UseCases.Any(useCase => !PostReprocessingConstants.UseCases.Contains(useCase)))
            return AppErrorFactory.Validation($"The use cases are {string.Join(", ", PostReprocessingConstants.UseCases)}.");
        return request.PromptBefore is <= 0 ? AppErrorFactory.Validation("The prompt version is positive.") : null;
    }

    /// <summary>Prices a use case's queued posts from the inference log: the newest production calls of the last 30 days on
    /// the use case's first configured route, summed per subject and averaged.</summary>
    private async Task<PostRerunCounts> EstimateAsync(string useCase, PostRerunCounts counts, DateTimeOffset now, CancellationToken ct)
    {
        string? model = (useCase == PostTagsConstants.UseCase ? tagOptions.Value.Models : extractionOptions.Value.Models).FirstOrDefault();
        counts = counts with { Model = model };
        if (model is null || services.GetService<IInferencesBroker>() is not { } inferences
            || await inferences.ListAsync(new InferenceQuery { UseCase = useCase, FromUtc = now - CostWindow, Limit = CostSample }, ct)
                is not Result<IReadOnlyList<InferenceModel>>.Success { Value: var rows })
            return counts;
        var perPost = rows.Where(row => row.EvalRunId is null && $"{row.Provider}/{row.Model}" == model)
            .GroupBy(row => row.Subject ?? row.Id.ToString("D"), StringComparer.Ordinal)
            .Select(post => post.Sum(row => row.CostMicroUsd))
            .ToArray();
        if (perPost.Length == 0)
            return counts;
        long cost = (long)Math.Round(perPost.Average());
        return counts with { CostPerPostMicroUsd = cost, CostBasis = perPost.Length, EstimatedCostMicroUsd = cost * counts.Queued };
    }

    private static Result<T> Invalid<T>(string message) where T : notnull => Result<T>.Fail(AppErrorFactory.Validation(message));

    [GeneratedRegex("^[a-z0-9]+(-[a-z0-9]+)*$", RegexOptions.CultureInvariant)]
    private static partial Regex SetKey();
}
