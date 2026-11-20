using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Client.Delivery;
using WoW.Two.Sdk.Beta.Be.Harvest.Posts;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Client.Sources;

/// <summary>Reads the console's Sources area over the running Harvest service.</summary>
internal sealed class ApiSourceOverviewBroker(HttpClient http) : ISourceOverviewBroker
{
    /// <inheritdoc />
    public Task<Result<IReadOnlyList<SourceOverviewModel>>> ListOverviewAsync(Guid containerId, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<IReadOnlyList<SourceOverviewModel>>(http, HttpMethod.Get,
            $"{SourceRouteConstants.Overview}?{RegistrationRouteConstants.ContainerIdParameter}={containerId}", null, cancellationToken);

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<SourcePostModel>>> ListSourcePostsAsync(Guid sourceId, int limit = SourceConstants.DefaultPostsLimit, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<IReadOnlyList<SourcePostModel>>(http, HttpMethod.Get,
            $"{RegistrationRouteConstants.Sources}/{sourceId}/{SourceRouteConstants.Posts}?{SourceRouteConstants.LimitParameter}={limit}", null, cancellationToken);

    /// <inheritdoc />
    public Task<Result<SourcePostPage>> QuerySourcePostsAsync(Guid sourceId, SourcePostQuery query, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<SourcePostPage>(http, HttpMethod.Post,
            $"{RegistrationRouteConstants.Sources}/{sourceId}/{SourceRouteConstants.PostQuery}", query, cancellationToken);

    /// <inheritdoc />
    public Task<Result<SourceProcessedPage>> QuerySourceProcessedAsync(Guid sourceId, SourceProcessedQuery query, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(query);
        var parameters = new List<string> { $"{SourceRouteConstants.LimitParameter}={query.Limit}" };
        if (query.EntityType is { } type)
            parameters.Add($"{SourceRouteConstants.EntityTypeParameter}={Uri.EscapeDataString(type)}");
        if (query.Verdict is { } verdict)
            parameters.Add($"{SourceRouteConstants.VerdictParameter}={verdict.ToString().ToLowerInvariant()}");
        if (query.Extracted is { } extracted)
            parameters.Add($"{SourceRouteConstants.ExtractedParameter}={(extracted ? "true" : "false")}");
        if (query.Cursor is { } cursor)
            parameters.Add($"{SourceRouteConstants.CursorParameter}={Uri.EscapeDataString(cursor)}");
        return HarvestDataHttp.SendAsync<SourceProcessedPage>(http, HttpMethod.Get,
            $"{RegistrationRouteConstants.Sources}/{sourceId}/{SourceRouteConstants.Processed}?{string.Join('&', parameters)}", null, cancellationToken);
    }

    /// <inheritdoc />
    public Task<Result<SourceProcessedPostDetailsModel>> GetSourceProcessedPostAsync(Guid sourceId, Guid postId, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<SourceProcessedPostDetailsModel>(http, HttpMethod.Get,
            $"{RegistrationRouteConstants.Sources}/{sourceId}/{SourceRouteConstants.Processed}/{postId}", null, cancellationToken);

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<PostRevisionModel>>> ListPostRevisionsAsync(Guid containerId, Guid postId, CancellationToken cancellationToken = default) =>
        HarvestDataHttp.SendAsync<IReadOnlyList<PostRevisionModel>>(http, HttpMethod.Get,
            $"{PostRouteConstants.Posts}/{postId}/{SourceRouteConstants.Revisions}?{RegistrationRouteConstants.ContainerIdParameter}={containerId}",
            null, cancellationToken);
}
