using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Runs;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Runs;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Testing.Sources;

/// <summary>Reads the console's Sources area over the test kit's fakes, by the same rules as the module.</summary>
/// <param name="registry">The registry fake that holds the sources, groups and pipelines.</param>
/// <param name="runs">The run fake that holds the runs and reading paces.</param>
/// <param name="posts">The post fake that holds the links and revisions.</param>
/// <param name="delivery">The delivery broker whose active subscriptions count as subscribers; none when omitted.</param>
/// <param name="time">The clock that decides today; the system clock when omitted.</param>
public sealed class FakeSourceOverviewBroker(
    FakeRegistrationBroker registry,
    FakeRunBroker runs,
    FakePostBroker posts,
    IDeliveryBroker? delivery = null,
    TimeProvider? time = null) : ISourceOverviewBroker
{
    private readonly Dictionary<Guid, ProcessedAnswerSeed> answers = [];
    private readonly Dictionary<string, DateTimeOffset> pauses = new(StringComparer.Ordinal);

    private TimeProvider Clock => time ?? TimeProvider.System;

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<SourceOverviewModel>>> ListOverviewAsync(Guid containerId, CancellationToken cancellationToken = default)
    {
        if (await registry.GetContainerAsync(containerId, cancellationToken) is not Result<ContainerModel>.Success
            || await registry.ListSourcesAsync(containerId, cancellationToken) is not Result<IReadOnlyList<SourceModel>>.Success { Value: var sources }
            || await registry.ListPipelinesAsync(containerId, cancellationToken) is not Result<IReadOnlyList<PipelineModel>>.Success { Value: var pipelines })
            return Result<IReadOnlyList<SourceOverviewModel>>.Fail(RegistrationErrors.ContainerNotFound(containerId));
        var now = Clock.GetUtcNow();
        var subscriptions = delivery is not null
            && await delivery.ListSubscriptionsAsync(null, cancellationToken) is Result<IReadOnlyList<SubscriptionModel>>.Success { Value: var all }
            ? all.Where(subscription => subscription.State == SubscriptionState.Active).ToList()
            : [];
        var allRuns = runs.Snapshot().Where(run => run.ContainerId == containerId).ToList();
        IReadOnlyList<FakePostBroker.LinkView> links;
        Dictionary<(Guid Post, Guid Source), Guid?> firstPipelines;
        Dictionary<Guid, int> processed;
        lock (posts.Gate)
        {
            links = posts.Links(containerId);
            firstPipelines = FirstPipelines(links.Where(link => posts.Find(link.PostId) is { MergedInto: null }));
            processed = links.Where(link => answers.ContainsKey(link.PostId) && posts.Find(link.PostId) is { MergedInto: null })
                .GroupBy(link => link.SourceId).ToDictionary(group => group.Key, group => group.Select(link => link.PostId).Distinct().Count());
        }

        var overviews = new List<SourceOverviewModel>();
        foreach (var source in sources)
        {
            var own = pipelines.Where(pipeline => pipeline.SourceId == source.Id).OrderBy(pipeline => pipeline.CreatedAtUtc).ThenBy(pipeline => pipeline.Id).ToList();
            var ids = own.Select(pipeline => pipeline.Id).ToHashSet();
            var sourceRuns = allRuns.Where(run => ids.Contains(run.PipelineId)).OrderByDescending(run => run.QueuedAtUtc).ThenByDescending(run => run.Id).ToList();
            var readable = SourceHarvestSelection.CanRead(source);
            var sections = own.Select(pipeline => Section(pipeline, source, sourceRuns, firstPipelines, subscriptions)).ToList();
            var health = await runs.HealthAsync(source, cancellationToken);
            DateTimeOffset pausedUntil;
            bool hostPaused;
            lock (posts.Gate)
                hostPaused = pauses.TryGetValue(health.HostKey, out pausedUntil);
            if (hostPaused && pausedUntil > now)
                health = health with { PausedUntil = pausedUntil, PauseReason = "Challenge" };
            var capture = sourceRuns.FirstOrDefault(run => SourceHarvestSelection.IsCollection(run.Kind));
            var lastHarvest = capture is null ? null : new SourceHarvestModel
            {
                RunId = capture.Id, PipelineId = capture.PipelineId, Kind = capture.Kind, At = capture.FinishedAtUtc ?? capture.QueuedAtUtc, Status = capture.Status,
                Reason = Reason(capture.CompletionReason), NewPosts = capture.Captured, Found = capture.Discovered,
            };
            var state = !readable ? source.State == SourceState.Paused ? SourceHarvestState.Idle : SourceHarvestState.NeedsAttention
                : health.PausedUntil is not null ? SourceHarvestState.NeedsAttention
                : sourceRuns.Any(run => run.Status is RunStatus.Queued or RunStatus.Running) ? SourceHarvestState.Harvesting
                : health.ConsecutiveFailures >= 3 || lastHarvest?.Status == RunStatus.Failed ? SourceHarvestState.NeedsAttention
                : sections.Any(section => section.Harvesting) ? SourceHarvestState.Healthy : SourceHarvestState.Idle;
            overviews.Add(new SourceOverviewModel
            {
                SourceId = source.Id,
                Name = source.Name ?? source.Handle,
                Family = source.Family,
                Handle = source.Handle,
                GroupId = source.GroupId,
                State = state,
                SourceState = source.State,
                Permission = source.Permission,
                LastHarvest = lastHarvest,
                NextHarvestAt = sections.Where(section => section.Harvesting && section.NextDueAt is not null).Min(section => section.NextDueAt),
                PostCount = links.Where(link => link.SourceId == source.Id).Select(link => link.PostId).Distinct()
                    .Count(postId => posts.Find(postId) is { MergedInto: null }),
                ProcessedCount = processed.GetValueOrDefault(source.Id),
                NewPostsDaily = Daily(links.Where(link => link.SourceId == source.Id), now),
                Sections = sections,
                Health = health,
            });
        }

        return Result<IReadOnlyList<SourceOverviewModel>>.Ok([.. overviews
            .OrderBy(overview => overview.Name, StringComparer.OrdinalIgnoreCase).ThenBy(overview => overview.SourceId)]);
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<SourcePostModel>>> ListSourcePostsAsync(Guid sourceId, int limit = SourceConstants.DefaultPostsLimit, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > SourceConstants.MaxPostsLimit)
            return Result<IReadOnlyList<SourcePostModel>>.Fail(RegistrationErrors.Invalid($"The limit must be between 1 and {SourceConstants.MaxPostsLimit}."));
        if (await registry.GetSourceAsync(sourceId, cancellationToken) is not Result<SourceModel>.Success { Value: var source })
            return Result<IReadOnlyList<SourcePostModel>>.Fail(RegistrationErrors.SourceNotFound(sourceId));
        return Result<IReadOnlyList<SourcePostModel>>.Ok([.. SourcePosts(source).Take(limit)]);
    }

    /// <inheritdoc />
    public async Task<Result<SourcePostPage>> QuerySourcePostsAsync(Guid sourceId, SourcePostQuery query, CancellationToken cancellationToken = default)
    {
        if (await registry.GetSourceAsync(sourceId, cancellationToken) is not Result<SourceModel>.Success { Value: var source }
            || await registry.ListPipelinesAsync(source.ContainerId, cancellationToken) is not Result<IReadOnlyList<PipelineModel>>.Success { Value: var pipelines })
            return Result<SourcePostPage>.Fail(RegistrationErrors.SourceNotFound(sourceId));
        return SourcePostQuerySelection.Start(source, query, Clock.GetUtcNow(), pipelines)
            .Match(selection =>
            {
                foreach (var post in SourcePosts(source))
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    selection.Add(post);
                }
                return Result<SourcePostPage>.Ok(selection.Complete());
            }, Result<SourcePostPage>.Fail);
    }

    /// <inheritdoc />
    public async Task<Result<SourceProcessedPage>> QuerySourceProcessedAsync(Guid sourceId, SourceProcessedQuery query,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        if (SourceProcessedQueryRules.Check(sourceId, query, out var after) is { } invalid)
            return Result<SourceProcessedPage>.Fail(invalid);
        if (await registry.GetSourceAsync(sourceId, cancellationToken) is not Result<SourceModel>.Success { Value: var source })
            return Result<SourceProcessedPage>.Fail(RegistrationErrors.SourceNotFound(sourceId));

        var matches = new List<SourceProcessedPostModel>();
        foreach (var listed in SourcePosts(source))
        {
            ProcessedAnswerSeed? seed;
            PostClassification? classification;
            lock (posts.Gate)
            {
                seed = answers.GetValueOrDefault(listed.PostId);
                classification = posts.Find(listed.PostId)?.Base.Classification;
            }
            if (seed is null)
                continue;
            var post = Processed(listed, seed, classification);
            if ((query.EntityType is null || post.Tags.EntityType == query.EntityType)
                && (query.Verdict is null || post.Classification?.Verdict == query.Verdict)
                && (query.Extracted is null || (post.Extraction?.Status == "done") == query.Extracted))
                matches.Add(post);
        }
        var page = matches.Where(post => SourceProcessedQueryRules.After(post, after)).Take(query.Limit + 1).ToList();
        var items = page.Take(query.Limit).ToList();
        return Result<SourceProcessedPage>.Ok(new SourceProcessedPage(items,
            page.Count > query.Limit ? SourceProcessedQueryRules.Cursor(sourceId, query, items[^1]) : null, matches.Count));
    }

    /// <summary>Pauses a host until a moment, as a refusal would leave it, so the overview of every source on it reports the
    /// pause; the pause shows only while it lasts.</summary>
    /// <param name="hostKey">The host key, a source's handle in the fake.</param>
    /// <param name="until">When the pause ends.</param>
    public void SeedHostPause(string hostKey, DateTimeOffset until)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(hostKey);
        lock (posts.Gate)
            pauses[hostKey] = until;
    }

    /// <summary>Stores a post's AI answers, as the passes would have left them; a later seed for the post replaces it.</summary>
    /// <param name="seed">The answers.</param>
    public void SeedAnswers(ProcessedAnswerSeed seed)
    {
        ArgumentNullException.ThrowIfNull(seed);
        lock (posts.Gate)
            answers[seed.PostId] = seed;
    }

    private static SourceProcessedPostModel Processed(SourcePostModel post, ProcessedAnswerSeed seed, PostClassification? classification) => new()
    {
        PostId = post.PostId,
        Title = post.Title,
        Url = post.Url,
        Revision = post.Revision,
        FirstSeenAt = post.FirstSeenAt,
        Kind = post.Kind,
        State = post.State,
        PostedAt = post.PostedAt,
        Language = post.Language,
        PipelineId = post.PipelineId,
        ImageUrl = post.ImageUrl,
        Classification = classification,
        Tags = new ProcessedTagsModel
        {
            Status = seed.Status, Error = seed.Error, Current = true, EntityType = seed.EntityType,
            Domains = [.. seed.Domains.Select((key, index) => new ProcessedDomainModel(key, key, index == 0))],
            Topics = seed.Topics, About = seed.About, Timeframe = seed.Timeframe, TitleEn = seed.TitleEn,
            Confidence = seed.Confidence, Flags = seed.Flags, Answer = Answer(seed.Model, seed.PromptVersion, seed.AnsweredAtUtc),
        },
        Extraction = seed.Extraction is { } extraction
            && extraction.UseCase == (seed.EntityType == "event" ? "extract-event" : seed.EntityType == "listing" ? "extract-listing" : null)
            ? new ProcessedExtractionModel
            {
                UseCase = extraction.UseCase, Status = extraction.Status, Role = extraction.Role,
                Event = extraction.Status == "done" ? extraction.Event : null,
                Listing = extraction.Status == "done" ? extraction.Listing : null,
                Flags = extraction.Flags, Answer = Answer(extraction.Model, extraction.PromptVersion, seed.AnsweredAtUtc),
            }
            : null,
    };

    /// <summary>The fake keeps no inference log, so an answer has no cost, latency or token counts.</summary>
    private static ProcessedAnswerModel Answer(string? route, int? promptVersion, DateTimeOffset at)
    {
        var slash = route?.IndexOf('/', StringComparison.Ordinal) ?? -1;
        return new ProcessedAnswerModel
        {
            Provider = slash > 0 ? route![..slash] : null, Model = slash > 0 ? route![(slash + 1)..] : route,
            PromptVersion = promptVersion, AnsweredAt = at,
        };
    }

    private IReadOnlyList<SourcePostModel> SourcePosts(SourceModel source)
    {
        lock (posts.Gate)
        {
            var links = posts.Links(source.ContainerId).Where(link => link.SourceId == source.Id).ToList();
            var firstPipelines = FirstPipelines(links);
            var list = new List<SourcePostModel>();
            foreach (var group in links.GroupBy(link => link.PostId))
            {
                if (posts.Find(group.Key) is not { MergedInto: null } post)
                    continue;
                var first = group.OrderBy(link => link.FirstSeen).ThenBy(link => link.Id).First();
                list.Add(new SourcePostModel
                {
                    PostId = post.Id,
                    Title = string.IsNullOrWhiteSpace(post.Base.Title) ? first.Url : post.Base.Title,
                    Kind = post.Base.Kind ?? PostKind.Other,
                    State = post.State,
                    Revision = post.Revision,
                    FirstSeenAt = post.FirstSeenAtUtc,
                    LastChangedAt = post.LastChangedAtUtc,
                    PipelineId = firstPipelines.GetValueOrDefault((post.Id, source.Id)),
                    StartsAt = post.Base.Dates.FirstOrDefault(date => date.Role == DateRole.Start)?.Value,
                    EndsAt = post.Base.Dates.FirstOrDefault(date => date.Role == DateRole.End)?.Value,
                    PostedAt = post.Base.PostedAt?.Value,
                    Language = post.Base.Language is { Code: var code } && code != "und" ? code : null,
                    ImageUrls = [.. post.Base.Media.Where(media => media.Kind == MediaKind.Image).Select(media => media.Url).Take(SourceConstants.MaxPostImages)],
                    Url = first.Url,
                    ImageUrl = post.Base.Media.FirstOrDefault(media => media.Kind == MediaKind.Image)?.Url,
                });
            }

            return [.. list.OrderByDescending(post => post.FirstSeenAt).ThenByDescending(post => post.PostId)];
        }
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<PostRevisionModel>>> ListPostRevisionsAsync(Guid containerId, Guid postId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (posts.Gate)
            return Task.FromResult(posts.Find(postId) is { } post && post.ContainerId == containerId
                ? Result<IReadOnlyList<PostRevisionModel>>.Ok([.. posts.Revisions(postId).OrderByDescending(revision => revision.Revision)])
                : Result<IReadOnlyList<PostRevisionModel>>.Fail(AppErrorFactory.NotFound("Post not found in the container.")));
    }

    private static SourceSectionModel Section(PipelineModel pipeline, SourceModel source, IReadOnlyList<RunModel> sourceRuns,
        IReadOnlyDictionary<(Guid Post, Guid Source), Guid?> firstPipelines, IReadOnlyList<SubscriptionModel> subscriptions)
    {
        var capture = pipeline.Schedules.FirstOrDefault(row => row.Kind == WorkKind.Capture && row.Enabled);
        var check = pipeline.Schedules.FirstOrDefault(row => row.Kind == WorkKind.Check && row.Enabled);
        var readable = SourceHarvestSelection.CanRead(source);
        var harvesting = readable && pipeline.State == PipelineState.Active && SourceHarvestSelection.Automatic(pipeline).Any();
        var next = harvesting ? SourceHarvestSelection.Automatic(pipeline).Where(schedule => schedule.NextDueAtUtc is not null)
            .OrderBy(schedule => schedule.NextDueAtUtc).FirstOrDefault() : null;
        var last = sourceRuns.FirstOrDefault(run => run.PipelineId == pipeline.Id);
        return new SourceSectionModel
        {
            PipelineId = pipeline.Id,
            Name = pipeline.Name,
            Url = string.IsNullOrWhiteSpace(pipeline.Scope.Url) ? null : pipeline.Scope.Url,
            Harvesting = harvesting,
            Paused = pipeline.State == PipelineState.Paused,
            HarvestKind = readable ? SourceHarvestSelection.ManualKind(pipeline) : null,
            NextWorkKind = next?.Kind,
            CaptureEvery = capture?.Every,
            CheckEvery = check?.Every,
            MaxPages = pipeline.Limits.MaxPages,
            LastRun = last is null ? null : new SourceSectionRunModel
            {
                RunId = last.Id, Kind = last.Kind, At = last.FinishedAtUtc ?? last.QueuedAtUtc, Status = last.Status,
                Reason = Reason(last.CompletionReason), NewPosts = last.Captured, Found = last.Discovered,
            },
            NextDueAt = next?.NextDueAtUtc,
            Posts = firstPipelines.Count(pair => pair.Key.Source == source.Id && pair.Value == pipeline.Id),
            Subscribers = subscriptions.Count(subscription => subscription.PipelineId == pipeline.Id),
        };
    }

    /// <summary>Gets, per post and source, the pipeline whose run made the earliest first-seen link — the fake's first
    /// capture; call under the post fake's gate.</summary>
    private Dictionary<(Guid Post, Guid Source), Guid?> FirstPipelines(IEnumerable<FakePostBroker.LinkView> links) =>
        links.GroupBy(link => (link.PostId, link.SourceId))
            .ToDictionary(group => (group.Key.PostId, group.Key.SourceId),
                group => posts.PipelineFor(group.OrderBy(link => link.FirstSeen).ThenBy(link => link.Id).First().FirstRun));

    private static IReadOnlyList<int> Daily(IEnumerable<FakePostBroker.LinkView> links, DateTimeOffset now)
    {
        var today = DateOnly.FromDateTime(now.UtcDateTime);
        var counts = links.GroupBy(link => DateOnly.FromDateTime(link.FirstSeen.UtcDateTime)).ToDictionary(group => group.Key, group => group.Count());
        return [.. Enumerable.Range(0, SourceConstants.OverviewDays)
            .Select(index => counts.GetValueOrDefault(today.AddDays(index - (SourceConstants.OverviewDays - 1))))];
    }

    /// <summary>Names a run reason as the store does: snake_case.</summary>
    private static string? Reason(RunReason? reason) => reason is { } value
        ? string.Concat(value.ToString().Select((letter, index) => index > 0 && char.IsUpper(letter) ? "_" + char.ToLowerInvariant(letter) : char.ToLowerInvariant(letter).ToString()))
        : null;
}
