using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Extraction;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Readers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.Harvest.Testing.Delivery;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Testing.Posts;

/// <summary>Implements the post contract in memory for consumer tests; no database or transport is involved.</summary>
public sealed class FakePostBroker(IRegistrationBroker registry, TimeProvider? clock = null) : IPostBroker
{
    private readonly TimeProvider time = clock ?? TimeProvider.System;
    private readonly Dictionary<Guid, PostModel> posts = [];
    private readonly Dictionary<Guid, LinkEntry> links = [];
    private readonly Dictionary<Guid, (PostMergeModel Merge, Guid Container, Guid[] Links)> merges = [];
    private readonly HashSet<Guid> keptApart = [];
    private readonly HashSet<(Guid Link, Guid Run)> observed = [];
    private readonly Dictionary<Guid, (Guid Container, Guid Source, Guid Pipeline)> runs = [];
    internal object Gate { get; } = new();
    internal event Action<PostModel, string, Guid?, Guid?>? Changed;

    /// <summary>Registers a fake run so capture and observation calls can validate their run reference.</summary>
    public void RegisterRun(Guid runId, Guid containerId, Guid sourceId, Guid pipelineId)
    {
        lock (Gate) runs[runId] = (containerId, sourceId, pipelineId);
    }

    /// <inheritdoc />
    public async Task<Result<CaptureSaveResult>> SaveCaptureAsync(Guid containerId, Guid sourceId, Guid? runId, Capture capture, PostEnvelope envelope, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (capture is null || envelope?.Extraction is null || capture.ContentProjection is null || string.IsNullOrWhiteSpace(capture.ExternalKey)
            || !Web(capture.Url) || !Web(capture.RequestUrl) || !Web(capture.FinalUrl) || capture.CapturedAtUtc.Offset != TimeSpan.Zero)
            return Invalid<CaptureSaveResult>("A capture requires its identity, HTTP(S) URLs, UTC timestamp and extraction stamp.");
        if (await registry.GetSourceAsync(sourceId, cancellationToken) is not Result<SourceModel>.Success { Value: var source } || source.ContainerId != containerId)
            return Missing<CaptureSaveResult>("Source not found in the container.");
        lock (Gate)
        {
            if (!ValidRun(runId, containerId, sourceId)) return Invalid<CaptureSaveResult>("The run must belong to its source and container.");
            var now = time.GetUtcNow();
            var entry = links.Values.SingleOrDefault(link => link.SourceId == sourceId && link.Key == capture.ExternalKey);
            var isNew = entry is null;
            var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(capture.ContentProjection)));
            var isNewPost = false;
            if (entry is null)
            {
                var candidates = links.Values.Where(link => link.ContainerId == containerId && !keptApart.Contains(link.PostId)
                    && (string.IsNullOrWhiteSpace(capture.OriginKey) || link.Origin is null || link.Origin == capture.OriginKey)
                    && (link.Url == capture.Url || (capture.OriginKey is not null && link.Origin == capture.OriginKey)
                        || (capture.ContentProjection.Length >= 64 && link.Hash == hash)))
                    .Select(link => link.PostId).Distinct().Take(2).ToArray();
                var postId = candidates.Length == 1 ? candidates[0] : Guid.CreateVersion7(now);
                isNewPost = !posts.ContainsKey(postId);
                if (isNewPost)
                    posts[postId] = new PostModel { Id = postId, ContainerId = containerId, State = PostState.Live, Revision = 1,
                        Base = envelope, FirstSeenAtUtc = now, LastChangedAtUtc = now };
                entry = new LinkEntry { Id = Guid.CreateVersion7(now), ContainerId = containerId, PostId = postId, SourceId = sourceId, Key = capture.ExternalKey,
                    Url = capture.Url, State = LinkState.Live, StateSince = now, Role = capture.Role, Envelope = envelope, Authority = source.Authority, Checkable = source.CheckPolicy != "none" };
                links.Add(entry.Id, entry);
            }
            if ((entry.At is { } at && capture.CapturedAtUtc < at)
                || (entry.State is LinkState.Suspect or LinkState.Gone && capture.CapturedAtUtc <= entry.StateSince))
                return Result<CaptureSaveResult>.Ok(new CaptureSaveResult { Post = Read(entry.PostId), LinkId = entry.Id, Changed = false });
            var changed = entry.Hash != hash;
            var restored = entry.State is LinkState.Suspect or LinkState.Gone;
            Guid? captureId = null;
            if (changed)
            {
                captureId = Guid.CreateVersion7(now); entry.CaptureId = captureId; entry.At = capture.CapturedAtUtc;
                entry.Hash = hash; entry.Envelope = envelope; entry.Origin = capture.OriginKey; entry.Url = capture.Url;
            }
            if (restored) { entry.State = LinkState.Relisted; entry.StateSince = now; }
            entry.Missing = 0;
            if (changed || restored || isNew)
            {
                Resolve(entry.PostId, !isNewPost);
                Changed?.Invoke(Read(entry.PostId), isNewPost ? "post.new" : "post.changed", runId, entry.Id);
            }
            return Result<CaptureSaveResult>.Ok(new CaptureSaveResult { Post = Read(entry.PostId), LinkId = entry.Id, CaptureId = captureId, Changed = changed });
        }
    }

    /// <inheritdoc />
    public async Task<Result<PostModel>> ObserveLinkAsync(Guid containerId, Guid sourceId, LinkObservation observation, Guid? runId = null, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!Enum.IsDefined(observation.Signal) || !Reasons.Contains(observation.Reason)) return Invalid<PostModel>("Invalid observation.");
        var sourceResult = await registry.GetSourceAsync(sourceId, cancellationToken);
        lock (Gate)
        {
            var entry = links.Values.SingleOrDefault(link => link.ContainerId == containerId && link.SourceId == sourceId && link.Key == observation.ExternalKey);
            if (entry is null) return Missing<PostModel>("Link not found in the source and container.");
            if (!ValidRun(runId, containerId, sourceId)) return Invalid<PostModel>("The run must belong to its source and container.");
            if (observation.Signal != LinkSignal.Present && (!entry.Checkable || sourceResult is not Result<SourceModel>.Success { Value.State: SourceState.Active }))
                return Result<PostModel>.Ok(Read(entry.PostId));
            if (runId is { } run && !observed.Add((entry.Id, run))) return Result<PostModel>.Ok(Read(entry.PostId));
            entry.Missing = observation.Signal == LinkSignal.Present ? 0 : entry.Missing + 1;
            var next = observation.Signal switch
            {
                LinkSignal.Present when entry.State == LinkState.Gone => LinkState.Relisted,
                LinkSignal.Present => LinkState.Live,
                LinkSignal.StrongNegative when observation.Reason is "direct_410" or "compliance_delete" or "delete_update" => LinkState.Gone,
                LinkSignal.StrongNegative when entry.Missing >= 2 => LinkState.Gone,
                _ when entry.State == LinkState.Gone => LinkState.Gone,
                _ => LinkState.Suspect,
            };
            if (entry.State != next)
            {
                entry.State = next; entry.StateSince = time.GetUtcNow(); Resolve(entry.PostId, true);
                Changed?.Invoke(Read(entry.PostId), "link.state", runId, entry.Id);
            }
            return Result<PostModel>.Ok(Read(entry.PostId));
        }
    }

    /// <inheritdoc />
    public Task<Result<PostModel>> GetPostAsync(Guid containerId, Guid postId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Gate) return Task.FromResult(posts.TryGetValue(postId, out var post) && post.ContainerId == containerId
            ? Result<PostModel>.Ok(Read(postId)) : Missing<PostModel>("Post not found in the container."));
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<PostModel>>> ListPostsAsync(Guid containerId, int limit = 100, CancellationToken cancellationToken = default)
    {
        if (limit is < 1 or > 500) return Invalid<IReadOnlyList<PostModel>>("The limit must be between 1 and 500.");
        if (await registry.GetContainerAsync(containerId, cancellationToken) is not Result<WoW.Two.Sdk.Beta.Be.Harvest.Containers.ContainerModel>.Success)
            return Missing<IReadOnlyList<PostModel>>("Container not found.");
        lock (Gate) return Result<IReadOnlyList<PostModel>>.Ok(Snapshot(containerId).Where(post => post.MergedInto is null)
            .OrderByDescending(post => post.LastChangedAtUtc).ThenByDescending(post => post.Id).Take(limit).ToArray());
    }

    /// <inheritdoc />
    public Task<Result<PostMergeModel>> MergeAsync(Guid containerId, Guid survivorId, Guid loserId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Gate)
        {
            if (survivorId == loserId) return Task.FromResult(Invalid<PostMergeModel>("A post cannot merge into itself."));
            if (!posts.TryGetValue(survivorId, out var survivor) || !posts.TryGetValue(loserId, out var loser) || survivor.ContainerId != containerId || loser.ContainerId != containerId)
                return Task.FromResult(Missing<PostMergeModel>("Both posts must belong to the specified container."));
            if (survivor.MergedInto is not null || loser.MergedInto is not null || merges.Values.Any(value => !value.Merge.Undone
                    && (value.Merge.SurvivorId == survivorId || value.Merge.LoserId == survivorId || value.Merge.SurvivorId == loserId || value.Merge.LoserId == loserId)))
                return Task.FromResult(Conflict<PostMergeModel>("Undo the existing merge first."));
            var ids = links.Values.Where(link => link.PostId == loserId).Select(link => link.Id).ToArray();
            foreach (var id in ids) links[id].PostId = survivorId;
            posts[loserId] = loser with { MergedInto = survivorId, Revision = loser.Revision + 1, LastChangedAtUtc = time.GetUtcNow() };
            Resolve(survivorId, true);
            var merge = new PostMergeModel { Id = Guid.CreateVersion7(time.GetUtcNow()), SurvivorId = survivorId, LoserId = loserId, Undone = false };
            merges.Add(merge.Id, (merge, containerId, ids));
            Changed?.Invoke(Read(loserId), "post.merged", null, null); Changed?.Invoke(Read(survivorId), "post.changed", null, null);
            return Task.FromResult(Result<PostMergeModel>.Ok(merge));
        }
    }

    /// <inheritdoc />
    public Task<Result<PostMergeModel>> UnmergeAsync(Guid containerId, Guid mergeId, CancellationToken cancellationToken = default)
    {
        cancellationToken.ThrowIfCancellationRequested();
        lock (Gate)
        {
            if (!merges.TryGetValue(mergeId, out var state) || state.Container != containerId) return Task.FromResult(Missing<PostMergeModel>("Merge not found."));
            if (state.Merge.Undone) return Task.FromResult(Result<PostMergeModel>.Ok(state.Merge));
            foreach (var id in state.Links) links[id].PostId = state.Merge.LoserId;
            posts[state.Merge.LoserId] = posts[state.Merge.LoserId] with { MergedInto = null };
            keptApart.Add(state.Merge.SurvivorId); keptApart.Add(state.Merge.LoserId);
            Resolve(state.Merge.SurvivorId, true); Resolve(state.Merge.LoserId, true);
            var undone = state.Merge with { Undone = true }; merges[mergeId] = (undone, containerId, state.Links);
            Changed?.Invoke(Read(undone.SurvivorId), "post.unmerged", null, null); Changed?.Invoke(Read(undone.LoserId), "post.unmerged", null, null);
            return Task.FromResult(Result<PostMergeModel>.Ok(undone));
        }
    }

    internal IReadOnlyList<PostModel> Snapshot(Guid containerId) => posts.Values.Where(post => post.ContainerId == containerId).Select(post => Read(post.Id)).ToArray();
    internal Guid? PipelineFor(Guid? runId) => runId is { } id && runs.TryGetValue(id, out var run) ? run.Pipeline : null;

    private PostModel Read(Guid id) => posts[id] with { Links = links.Values.Where(link => link.PostId == id).Select(link => new PostLinkModel
    {
        Id = link.Id, SourceId = link.SourceId, ExternalKey = link.Key, Url = link.Url, State = link.State, Role = link.Role, CaptureId = link.CaptureId, CapturedAtUtc = link.At,
    }).ToArray() };

    private void Resolve(Guid postId, bool bump)
    {
        var rows = links.Values.Where(link => link.PostId == postId).OrderBy(link => link.State is LinkState.Live or LinkState.Relisted ? 0 : 1)
            .ThenByDescending(link => link.Authority).ThenByDescending(link => link.At).ThenBy(link => link.Id).ToArray();
        if (rows.Length == 0) return;
        var fields = new Dictionary<string, JsonElement>(); var provenance = new Dictionary<string, Guid>(); var extractionSources = new Dictionary<string, FieldSource>();
        foreach (var row in rows)
            foreach (var field in JsonSerializer.SerializeToElement(row.Envelope, TestWireJson.Options).EnumerateObject())
            {
                if (field.Name == "fieldSources" || fields.ContainsKey(field.Name) || field.Value.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined
                    || (field.Value.ValueKind == JsonValueKind.Array && field.Value.GetArrayLength() == 0)
                    || (field.Value.ValueKind == JsonValueKind.Object && !field.Value.EnumerateObject().Any())) continue;
                fields.Add(field.Name, field.Value.Clone());
                if (field.Name is not "schema" and not "extraction" and not "fieldSources") provenance.Add(field.Name, row.Id);
                if (row.Envelope.FieldSources.TryGetValue(field.Name, out var extractionSource)) extractionSources[field.Name] = extractionSource;
            }
        fields["fieldSources"] = JsonSerializer.SerializeToElement(extractionSources, TestWireJson.Options);
        var checkable = rows.Where(row => row.Checkable).ToArray();
        posts[postId] = posts[postId] with
        {
            Base = JsonSerializer.Deserialize<PostEnvelope>(JsonSerializer.Serialize(fields), TestWireJson.Options)!, FieldSources = provenance,
            Revision = posts[postId].Revision + (bump ? 1 : 0), LastChangedAtUtc = time.GetUtcNow(),
            State = checkable.Length == 0 || checkable.Any(row => row.State is LinkState.Live or LinkState.Relisted) ? PostState.Live
                : checkable.Any(row => row.State == LinkState.Suspect) ? PostState.Suspect : PostState.Gone,
        };
    }

    private bool ValidRun(Guid? runId, Guid containerId, Guid sourceId) => runId is null || runs.TryGetValue(runId.Value, out var run) && run.Container == containerId && run.Source == sourceId;
    private static bool Web(string value) => Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https" && string.IsNullOrEmpty(uri.UserInfo);
    private static Result<T> Invalid<T>(string message) where T : notnull => Result<T>.Fail(AppErrorFactory.Validation(message));
    private static Result<T> Missing<T>(string message) where T : notnull => Result<T>.Fail(AppErrorFactory.NotFound(message));
    private static Result<T> Conflict<T>(string message) where T : notnull => Result<T>.Fail(AppErrorFactory.Conflict(message));
    private static readonly HashSet<string> Reasons = ["direct_410", "direct_404", "named_redirect", "soft_404", "node_vanished", "absent_from_enumeration", "embed_not_found", "mtproto_empty", "delete_update", "provider_error_item", "compliance_delete", "api_missing", "seen_again", "manual"];
    private sealed class LinkEntry
    {
        internal required Guid Id, ContainerId, SourceId;
        internal Guid PostId;
        internal required string Key, Url;
        internal string? Hash, Origin;
        internal required PostEnvelope Envelope;
        internal int Authority, Missing;
        internal bool Checkable;
        internal LinkState State;
        internal LinkRole Role;
        internal Guid? CaptureId;
        internal DateTimeOffset? At;
        internal DateTimeOffset StateSince;
    }
}
