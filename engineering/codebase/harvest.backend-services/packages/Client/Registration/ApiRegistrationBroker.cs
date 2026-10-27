using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Foundation.Serialization;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Client.Registration;

/// <summary>Integrates the running Harvest service's registration API.</summary>
/// <remarks>Maps the service's problems back onto the contract: 400 → <c>Validation</c>, 401 → <c>Unauthorized</c>,
/// 403 → <c>Forbidden</c>, 404 → <c>NotFound</c>, 409 → <c>Conflict</c>; an unreachable or failing service is
/// <c>ExternalUnavailable</c>.</remarks>
internal sealed class ApiRegistrationBroker(HttpClient http) : IRegistrationBroker
{
    /// <inheritdoc />
    public Task<Result<TenantModel>> RegisterTenantAsync(TenantRegistration registration, CancellationToken cancellationToken = default) =>
        SendAsync<TenantModel>(HttpMethod.Post, RegistrationRouteConstants.Tenants, registration, cancellationToken);

    /// <inheritdoc />
    public Task<Result<TenantModel>> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        SendAsync<TenantModel>(HttpMethod.Get, $"{RegistrationRouteConstants.Tenants}/{tenantId}", null, cancellationToken);

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<TenantModel>>> ListTenantsAsync(CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<TenantModel>>(HttpMethod.Get, RegistrationRouteConstants.Tenants, null, cancellationToken);

    /// <inheritdoc />
    public Task<Result<IssuedApiKeyModel>> IssueApiKeyAsync(Guid tenantId, ApiKeyIssue issue, CancellationToken cancellationToken = default) =>
        SendAsync<IssuedApiKeyModel>(HttpMethod.Post, TenantKeys(tenantId), issue, cancellationToken);

    /// <inheritdoc />
    public Task<Result<ApiKeyModel>> GetApiKeyAsync(Guid keyId, CancellationToken cancellationToken = default) =>
        SendAsync<ApiKeyModel>(HttpMethod.Get, $"{RegistrationRouteConstants.ApiKeys}/{keyId}", null, cancellationToken);

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<ApiKeyModel>>> ListApiKeysAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<ApiKeyModel>>(HttpMethod.Get, TenantKeys(tenantId), null, cancellationToken);

    /// <inheritdoc />
    public Task<Result<ApiKeyModel>> RevokeApiKeyAsync(Guid tenantId, Guid keyId, CancellationToken cancellationToken = default) =>
        SendAsync<ApiKeyModel>(HttpMethod.Delete, $"{TenantKeys(tenantId)}/{keyId}", null, cancellationToken);

    /// <inheritdoc />
    public Task<Result<ContainerModel>> RegisterContainerAsync(ContainerRegistration registration, CancellationToken cancellationToken = default) =>
        SendAsync<ContainerModel>(HttpMethod.Post, RegistrationRouteConstants.Containers, registration, cancellationToken);

    /// <inheritdoc />
    public Task<Result<ContainerModel>> GetContainerAsync(Guid containerId, CancellationToken cancellationToken = default) =>
        SendAsync<ContainerModel>(HttpMethod.Get, $"{RegistrationRouteConstants.Containers}/{containerId}", null, cancellationToken);

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<ContainerModel>>> ListContainersAsync(Guid? tenantId = null, CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<ContainerModel>>(
            HttpMethod.Get,
            tenantId is { } id
                ? $"{RegistrationRouteConstants.Containers}?{RegistrationRouteConstants.TenantIdParameter}={id}"
                : RegistrationRouteConstants.Containers,
            null,
            cancellationToken);

    /// <inheritdoc />
    public Task<Result<SourceModel>> RegisterSourceAsync(SourceRegistration registration, CancellationToken cancellationToken = default) =>
        SendAsync<SourceModel>(HttpMethod.Post, RegistrationRouteConstants.Sources, registration, cancellationToken);

    /// <inheritdoc />
    public Task<Result<SourceModel>> GetSourceAsync(Guid sourceId, CancellationToken cancellationToken = default) =>
        SendAsync<SourceModel>(HttpMethod.Get, $"{RegistrationRouteConstants.Sources}/{sourceId}", null, cancellationToken);

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<SourceModel>>> ListSourcesAsync(Guid containerId, CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<SourceModel>>(HttpMethod.Get, ByContainer(RegistrationRouteConstants.Sources, containerId), null, cancellationToken);

    /// <inheritdoc />
    public Task<Result<PipelineModel>> DeclarePipelineAsync(PipelineDeclaration declaration, CancellationToken cancellationToken = default) =>
        SendAsync<PipelineModel>(HttpMethod.Post, RegistrationRouteConstants.Pipelines, declaration, cancellationToken);

    /// <inheritdoc />
    public Task<Result<PipelineModel>> GetPipelineAsync(Guid pipelineId, CancellationToken cancellationToken = default) =>
        SendAsync<PipelineModel>(HttpMethod.Get, $"{RegistrationRouteConstants.Pipelines}/{pipelineId}", null, cancellationToken);

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<PipelineModel>>> ListPipelinesAsync(Guid containerId, CancellationToken cancellationToken = default) =>
        SendAsync<IReadOnlyList<PipelineModel>>(HttpMethod.Get, ByContainer(RegistrationRouteConstants.Pipelines, containerId), null, cancellationToken);

    private static string TenantKeys(Guid tenantId) =>
        $"{RegistrationRouteConstants.Tenants}/{tenantId}/{RegistrationRouteConstants.TenantApiKeys}";

    private static string ByContainer(string resource, Guid containerId) =>
        $"{resource}?{RegistrationRouteConstants.ContainerIdParameter}={containerId}";

    /// <summary>Sends one call and maps the answer onto the contract's result.</summary>
    private async Task<Result<T>> SendAsync<T>(HttpMethod method, string path, object? body, CancellationToken cancellationToken) where T : notnull
    {
        try
        {
            using var request = new HttpRequestMessage(method, path);
            if (body is not null)
                request.Content = JsonContent.Create(body, body.GetType(), options: JsonOptionsConstants.Default);
            using var response = await http.SendAsync(request, cancellationToken);

            if (response.IsSuccessStatusCode)
            {
                var envelope = await response.Content.ReadFromJsonAsync<DataEnvelope<T>>(JsonOptionsConstants.Default, cancellationToken);
                return envelope is { Data: { } value }
                    ? Result<T>.Ok(value)
                    : Result<T>.Fail(AppErrorFactory.SerializationFailed("The Harvest service answered without data."));
            }

            var problem = await ReadProblemAsync(response, cancellationToken);
            var message = problem?.Detail ?? problem?.Title ?? $"The Harvest service answered {(int)response.StatusCode}.";
            return Result<T>.Fail(response.StatusCode switch
            {
                HttpStatusCode.BadRequest or HttpStatusCode.UnprocessableEntity => AppErrorFactory.Validation(message),
                HttpStatusCode.Unauthorized => AppErrorFactory.Unauthorized(message),
                HttpStatusCode.Forbidden => AppErrorFactory.Forbidden(message),
                HttpStatusCode.NotFound => AppErrorFactory.NotFound(message),
                HttpStatusCode.Conflict => AppErrorFactory.Conflict(message),
                _ => AppErrorFactory.ExternalUnavailable(message),
            });
        }
        catch (HttpRequestException)
        {
            return Result<T>.Fail(AppErrorFactory.ExternalUnavailable("The Harvest service is unreachable."));
        }
        catch (JsonException exception)
        {
            return Result<T>.Fail(AppErrorFactory.SerializationFailed("The Harvest service answered malformed JSON.", exception));
        }
    }

    /// <summary>Reads the problem body, or nothing when the response carries none.</summary>
    private static async Task<ProblemBody?> ReadProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        try
        {
            return response.Content.Headers.ContentLength is 0
                ? null
                : await response.Content.ReadFromJsonAsync<ProblemBody>(JsonOptionsConstants.Default, cancellationToken);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
