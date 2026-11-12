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
        lock (posts.Gate)
        {
            links = posts.Links(containerId);
            firstPipelines = FirstPipelines(links.Where(link => posts.Find(link.PostId) is { MergedInto: null }));
        }

        var overviews = new List<SourceOverviewModel>();
        foreach (var source in sources)
        {
            var own = pipelines.Where(pipeline => pipeline.SourceId == source.Id).OrderBy(pipeline => pipeline.CreatedAtUtc).ThenBy(pipeline => pipeline.Id).ToList();
            var ids = own.Select(pipeline => pipeline.Id).ToHashSet();
            var sourceRuns = allRuns.Where(run => ids.Contains(run.PipelineId)).OrderByDescending(run => run.QueuedAtUtc).ThenByDescending(run => run.Id).ToList();
            var sections = own.Select(pipeline => Section(pipeline, sourceRuns, firstPipelines, source.Id, subscriptions)).ToList();
            var health = await runs.HealthAsync(source, cancellationToken);
            var capture = sourceRuns.FirstOrDefault(run => run.Kind == WorkKind.Capture);
            var lastHarvest = capture is null ? null : new SourceHarvestModel
            {
                RunId = capture.Id, PipelineId = capture.PipelineId, At = capture.FinishedAtUtc ?? capture.QueuedAtUtc, Status = capture.Status,
                Reason = Reason(capture.CompletionReason), NewPosts = capture.Captured, Found = capture.Discovered,
            };
            var state = sourceRuns.Any(run => run.Status is RunStatus.Queued or RunStatus.Running) ? SourceHarvestState.Harvesting
                : health.PausedUntil is not null || health.ConsecutiveFailures >= 3 || lastHarvest?.Status == RunStatus.Failed ? SourceHarvestState.NeedsAttention
                : sections.Any(section => section.Harvesting) ? SourceHarvestState.Healthy : SourceHarvestState.Idle;
            overviews.Add(new SourceOverviewModel
            {
                SourceId = source.Id,
                Name = source.Name ?? source.Handle,
                Family = source.Family,
                Handle = source.Handle,
                GroupId = source.GroupId,
                State = state,
                LastHarvest = lastHarvest,
                NextHarvestAt = sections.Where(section => section.Harvesting && section.NextDueAt is not null).Min(section => section.NextDueAt),
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
        lock (posts.Gate)
        {
            var links = posts.Links(source.ContainerId).Where(link => link.SourceId == sourceId).ToList();
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
                    PipelineId = firstPipelines.GetValueOrDefault((post.Id, sourceId)),
                    StartsAt = post.Base.Dates.FirstOrDefault(date => date.Role == DateRole.Start)?.Value,
                    EndsAt = post.Base.Dates.FirstOrDefault(date => date.Role == DateRole.End)?.Value,
                    PostedAt = post.Base.PostedAt?.Value,
                    ImageUrls = [.. post.Base.Media.Where(media => media.Kind == MediaKind.Image).Select(media => media.Url).Take(SourceConstants.MaxPostImages)],
                    Url = first.Url,
                    ImageUrl = post.Base.Media.FirstOrDefault(media => media.Kind == MediaKind.Image)?.Url,
                });
            }

            return Result<IReadOnlyList<SourcePostModel>>.Ok([.. list.OrderByDescending(post => post.FirstSeenAt).ThenByDescending(post => post.PostId).Take(limit)]);
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

    private static SourceSectionModel Section(PipelineModel pipeline, IReadOnlyList<RunModel> sourceRuns,
        IReadOnlyDictionary<(Guid Post, Guid Source), Guid?> firstPipelines, Guid sourceId, IReadOnlyList<SubscriptionModel> subscriptions)
    {
        var capture = pipeline.Schedules.FirstOrDefault(row => row.Kind == WorkKind.Capture && row.Enabled);
        var check = pipeline.Schedules.FirstOrDefault(row => row.Kind == WorkKind.Check && row.Enabled);
        var harvesting = pipeline.State == PipelineState.Active && capture is not null;
        var last = sourceRuns.FirstOrDefault(run => run.PipelineId == pipeline.Id);
        return new SourceSectionModel
        {
            PipelineId = pipeline.Id,
            Name = pipeline.Name,
            Url = string.IsNullOrWhiteSpace(pipeline.Scope.Url) ? null : pipeline.Scope.Url,
            Harvesting = harvesting,
            Paused = pipeline.State == PipelineState.Paused,
            CaptureEvery = capture?.Every,
            CheckEvery = check?.Every,
            MaxPages = pipeline.Limits.MaxPages,
            LastRun = last is null ? null : new SourceSectionRunModel
            {
                RunId = last.Id, Kind = last.Kind, At = last.FinishedAtUtc ?? last.QueuedAtUtc, Status = last.Status,
                Reason = Reason(last.CompletionReason), NewPosts = last.Captured, Found = last.Discovered,
            },
            NextDueAt = harvesting ? capture!.NextDueAtUtc : null,
            Posts = firstPipelines.Count(pair => pair.Key.Source == sourceId && pair.Value == pipeline.Id),
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
