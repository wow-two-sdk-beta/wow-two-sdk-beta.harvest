using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Testing.Registration;

/// <summary>Integrates an in-memory stand-in for Harvest's registry, for a consumer's tests; it passes the conformance
/// suite, so a test may rely on it as on the module.</summary>
/// <param name="time">The clock rows are stamped with; the system clock when omitted.</param>
public sealed partial class FakeRegistrationBroker(TimeProvider? time = null) : IRegistrationBroker
{
    private const string Alphabet = "ABCDEFGHIJKLMNOPQRSTUVWXYZabcdefghijklmnopqrstuvwxyz0123456789";

    private static readonly HashSet<SourceFamily> WebFamilies =
    [
        SourceFamily.Page, SourceFamily.Structured, SourceFamily.Site, SourceFamily.Render, SourceFamily.Api,
        SourceFamily.Feed, SourceFamily.Link, SourceFamily.Listing,
    ];

    private readonly Lock gate = new();
    private readonly List<TenantModel> tenants = [];
    private readonly List<(ApiKeyModel Key, string Hash)> keys = [];
    private readonly List<ContainerModel> containers = [];
    private readonly List<SourceModel> sources = [];
    private readonly List<PipelineModel> pipelines = [];

    private TimeProvider Clock => time ?? TimeProvider.System;

    /// <summary>Gets every secret the fake issued, by key identifier — the fake's stand-in for a consumer's secret store.</summary>
    public IDictionary<Guid, string> IssuedSecrets { get; } = new Dictionary<Guid, string>();

    /// <inheritdoc />
    public Task<Result<TenantModel>> RegisterTenantAsync(TenantRegistration registration, CancellationToken cancellationToken = default)
    {
        var slug = registration.Slug?.Trim().ToLowerInvariant() ?? string.Empty;
        if (slug.Length is 0 or > RegistrationConstants.MaxSlugLength || !SlugPattern().IsMatch(slug))
            return Fail<TenantModel>(RegistrationErrors.Invalid("The slug is malformed."));
        if (Name(registration.Name, RegistrationConstants.MaxNameLength) is not { } name)
            return Fail<TenantModel>(RegistrationErrors.Invalid("The name is malformed."));

        lock (gate)
        {
            if (tenants.Any(tenant => tenant.Slug == slug))
                return Fail<TenantModel>(RegistrationErrors.TenantSlugTaken(slug));

            var now = Clock.GetUtcNow();
            var tenant = new TenantModel
            {
                Id = Guid.CreateVersion7(now),
                Slug = slug,
                Name = name,
                State = TenantState.Active,
                PlatformCredentialsAllowed = registration.PlatformCredentialsAllowed,
                CreatedAtUtc = now,
            };
            tenants.Add(tenant);
            return Ok(tenant);
        }
    }

    /// <inheritdoc />
    public Task<Result<TenantModel>> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        lock (gate)
            return tenants.FirstOrDefault(tenant => tenant.Id == tenantId) is { } tenant
                ? Ok(tenant)
                : Fail<TenantModel>(RegistrationErrors.TenantNotFound(tenantId));
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<TenantModel>>> ListTenantsAsync(CancellationToken cancellationToken = default)
    {
        lock (gate)
            return Ok<IReadOnlyList<TenantModel>>([.. tenants.OrderBy(tenant => tenant.Slug, StringComparer.Ordinal)]);
    }

    /// <inheritdoc />
    public Task<Result<IssuedApiKeyModel>> IssueApiKeyAsync(Guid tenantId, ApiKeyIssue issue, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (tenants.All(tenant => tenant.Id != tenantId))
                return Fail<IssuedApiKeyModel>(RegistrationErrors.TenantNotFound(tenantId));

            var now = Clock.GetUtcNow();
            if (Name(issue.Name, ApiKeyConstants.MaxNameLength) is not { } name)
                return Fail<IssuedApiKeyModel>(RegistrationErrors.Invalid("The key name is malformed."));
            if (issue.Scopes is not { Count: > 0 } || issue.Scopes.Any(scope => !HarvestScopeConstants.TenantScopes.Contains(scope)))
                return Fail<IssuedApiKeyModel>(RegistrationErrors.Invalid("A key grants one or more tenant scopes."));
            if (issue.ExpiresAtUtc is { } expires && expires <= now)
                return Fail<IssuedApiKeyModel>(RegistrationErrors.Invalid("A key must expire in the future."));

            var secret = ApiKeyConstants.Marker + new string(RandomNumberGenerator.GetItems<char>(Alphabet, ApiKeyConstants.RandomLength));
            var key = new ApiKeyModel
            {
                Id = Guid.CreateVersion7(now),
                TenantId = tenantId,
                Name = name,
                Prefix = secret[..(ApiKeyConstants.Marker.Length + 8)],
                Scopes = [.. issue.Scopes.Distinct(StringComparer.Ordinal)],
                State = ApiKeyState.Active,
                CreatedAtUtc = now,
                ExpiresAtUtc = issue.ExpiresAtUtc?.ToUniversalTime(),
            };
            keys.Add((key, Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes(secret)))));
            IssuedSecrets[key.Id] = secret;
            return Ok(new IssuedApiKeyModel { Key = key, Secret = secret });
        }
    }

    /// <inheritdoc />
    public Task<Result<ApiKeyModel>> GetApiKeyAsync(Guid keyId, CancellationToken cancellationToken = default)
    {
        lock (gate)
            return keys.FirstOrDefault(entry => entry.Key.Id == keyId).Key is { } key
                ? Ok(key)
                : Fail<ApiKeyModel>(RegistrationErrors.ApiKeyNotFound(keyId));
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<ApiKeyModel>>> ListApiKeysAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (tenants.All(tenant => tenant.Id != tenantId))
                return Fail<IReadOnlyList<ApiKeyModel>>(RegistrationErrors.TenantNotFound(tenantId));
            return Ok<IReadOnlyList<ApiKeyModel>>([.. keys.Select(entry => entry.Key).Where(key => key.TenantId == tenantId)]);
        }
    }

    /// <inheritdoc />
    public Task<Result<ApiKeyModel>> RevokeApiKeyAsync(Guid tenantId, Guid keyId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            var index = keys.FindIndex(entry => entry.Key.Id == keyId && entry.Key.TenantId == tenantId);
            if (index < 0)
                return Fail<ApiKeyModel>(RegistrationErrors.ApiKeyNotFound(keyId));
            if (keys[index].Key.State == ApiKeyState.Active)
                keys[index] = (keys[index].Key with { State = ApiKeyState.Revoked, RevokedAtUtc = Clock.GetUtcNow() }, keys[index].Hash);
            return Ok(keys[index].Key);
        }
    }

    /// <inheritdoc />
    public Task<Result<ContainerModel>> RegisterContainerAsync(ContainerRegistration registration, CancellationToken cancellationToken = default)
    {
        if ((registration.Kind == ContainerKind.Pool) != (registration.TenantId is null))
            return Fail<ContainerModel>(RegistrationErrors.Invalid("The pool has no tenant; a private container has one."));
        var externalRef = Optional(registration.ExternalRef);
        if (externalRef is { Length: > RegistrationConstants.MaxExternalRefLength })
            return Fail<ContainerModel>(RegistrationErrors.Invalid("The external reference is too long."));

        lock (gate)
        {
            if (registration.TenantId is { } tenantId && tenants.All(tenant => tenant.Id != tenantId))
                return Fail<ContainerModel>(RegistrationErrors.TenantNotFound(tenantId));
            if (registration.Kind == ContainerKind.Pool && containers.Any(container => container.Kind == ContainerKind.Pool))
                return Fail<ContainerModel>(RegistrationErrors.PoolExists());
            if (externalRef is not null && containers.Any(container => container.TenantId == registration.TenantId && container.ExternalRef == externalRef))
                return Fail<ContainerModel>(RegistrationErrors.ContainerRefTaken(externalRef));

            var now = Clock.GetUtcNow();
            var container = new ContainerModel
            {
                Id = Guid.CreateVersion7(now),
                Kind = registration.Kind,
                TenantId = registration.TenantId,
                ExternalRef = externalRef,
                Residency = Optional(registration.Residency),
                EmbeddingsEnabled = registration.EmbeddingsEnabled,
                State = ContainerState.Active,
                CreatedAtUtc = now,
            };
            containers.Add(container);
            return Ok(container);
        }
    }

    /// <inheritdoc />
    public Task<Result<ContainerModel>> GetContainerAsync(Guid containerId, CancellationToken cancellationToken = default)
    {
        lock (gate)
            return containers.FirstOrDefault(container => container.Id == containerId) is { } container
                ? Ok(container)
                : Fail<ContainerModel>(RegistrationErrors.ContainerNotFound(containerId));
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<ContainerModel>>> ListContainersAsync(Guid? tenantId = null, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (tenantId is { } id && tenants.All(tenant => tenant.Id != id))
                return Fail<IReadOnlyList<ContainerModel>>(RegistrationErrors.TenantNotFound(id));
            return Ok<IReadOnlyList<ContainerModel>>([.. containers.Where(container => tenantId is null || container.TenantId == tenantId)]);
        }
    }

    /// <inheritdoc />
    public Task<Result<SourceModel>> RegisterSourceAsync(SourceRegistration registration, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (containers.All(container => container.Id != registration.ContainerId))
                return Fail<SourceModel>(RegistrationErrors.ContainerNotFound(registration.ContainerId));
        }

        if (Handle(registration.Family, registration.Handle) is not { } handle)
            return Fail<SourceModel>(RegistrationErrors.Invalid("The handle is malformed."));
        if (registration.Authority is < 0 or > 100
            || !SourceConstants.RetentionClasses.Contains(registration.RetentionClass)
            || (registration.CheckPolicy is { } checkPolicy && !SourceConstants.CheckPolicies.Contains(checkPolicy))
            || (registration.CapabilityPolicy is { } policy && !IsValid(policy)))
            return Fail<SourceModel>(RegistrationErrors.Invalid("The source is malformed."));

        lock (gate)
        {
            if (sources.Any(source => source.ContainerId == registration.ContainerId && source.Family == registration.Family && source.Handle == handle))
                return Fail<SourceModel>(RegistrationErrors.SourceExists(handle));

            var now = Clock.GetUtcNow();
            var source = new SourceModel
            {
                Id = Guid.CreateVersion7(now),
                ContainerId = registration.ContainerId,
                Family = registration.Family,
                Handle = handle,
                ExternalRef = Optional(registration.ExternalRef),
                Coverage = registration.Coverage,
                Completeness = registration.Completeness,
                Posture = registration.Posture,
                Permission = registration.Permission,
                AiAllowed = registration.AiAllowed,
                PoolShareable = registration.PoolShareable,
                RobotsPolicy = registration.RobotsPolicy,
                ConsentBasis = registration.ConsentBasis,
                Authority = registration.Authority,
                RetentionClass = registration.RetentionClass,
                CheckPolicy = registration.CheckPolicy,
                CapabilityPolicy = registration.CapabilityPolicy ?? new CapabilityPolicy(),
                State = SourceState.Active,
                StateSinceUtc = now,
                CreatedAtUtc = now,
            };
            sources.Add(source);
            return Ok(source);
        }
    }

    /// <inheritdoc />
    public Task<Result<SourceModel>> GetSourceAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        lock (gate)
            return sources.FirstOrDefault(source => source.Id == sourceId) is { } source
                ? Ok(source)
                : Fail<SourceModel>(RegistrationErrors.SourceNotFound(sourceId));
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<SourceModel>>> ListSourcesAsync(Guid containerId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (containers.All(container => container.Id != containerId))
                return Fail<IReadOnlyList<SourceModel>>(RegistrationErrors.ContainerNotFound(containerId));
            return Ok<IReadOnlyList<SourceModel>>([.. sources
                .Where(source => source.ContainerId == containerId)
                .OrderBy(source => source.Family).ThenBy(source => source.Handle, StringComparer.Ordinal)]);
        }
    }

    /// <inheritdoc />
    public Task<Result<PipelineModel>> DeclarePipelineAsync(PipelineDeclaration declaration, CancellationToken cancellationToken = default)
    {
        SourceModel? source;
        lock (gate)
        {
            if (containers.All(container => container.Id != declaration.ContainerId))
                return Fail<PipelineModel>(RegistrationErrors.ContainerNotFound(declaration.ContainerId));
            source = sources.FirstOrDefault(candidate => candidate.Id == declaration.SourceId);
        }

        if (source is null)
            return Fail<PipelineModel>(RegistrationErrors.SourceNotFound(declaration.SourceId));
        var scopeKey = ScopeKey(declaration.Scope);
        if (source.ContainerId != declaration.ContainerId || source.Permission == SourcePermission.Denied || scopeKey is null
            || (source.Coverage == SourceCoverage.Filtered && scopeKey == PipelineConstants.WholeScopeKey)
            || (declaration.Name is { } name && Name(name, PipelineConstants.MaxNameLength) is null)
            || !IsValid(declaration.Schedules) || !IsValid(declaration.Steps) || !IsValid(declaration.Limits))
            return Fail<PipelineModel>(RegistrationErrors.Invalid("The pipeline is malformed."));

        lock (gate)
        {
            if (pipelines.Any(pipeline => pipeline.ContainerId == declaration.ContainerId && pipeline.SourceId == declaration.SourceId && pipeline.ScopeKey == scopeKey))
                return Fail<PipelineModel>(RegistrationErrors.PipelineExists(scopeKey));

            var now = Clock.GetUtcNow();
            var pipeline = new PipelineModel
            {
                Id = Guid.CreateVersion7(now),
                ContainerId = declaration.ContainerId,
                SourceId = declaration.SourceId,
                ScopeKey = scopeKey,
                Scope = declaration.Scope ?? new PipelineScope(),
                Name = declaration.Name?.Trim(),
                State = source.Permission is SourcePermission.Open or SourcePermission.Granted ? PipelineState.Active : PipelineState.PendingApproval,
                Pinned = declaration.Pinned,
                Ai = declaration.Ai,
                Limits = declaration.Limits,
                Steps = declaration.Steps,
                Schedules = [.. declaration.Schedules.OrderBy(schedule => schedule.Kind).Select(schedule => new ScheduleModel
                {
                    Id = Guid.CreateVersion7(now),
                    Kind = schedule.Kind,
                    Mode = schedule.Mode,
                    Every = schedule.Every,
                    MaxEvery = schedule.MaxEvery,
                    Cron = schedule.Cron?.Trim(),
                    TimeZone = schedule.TimeZone,
                    Window = schedule.Window,
                    Enabled = schedule.Enabled,
                })],
                CreatedAtUtc = now,
            };
            pipelines.Add(pipeline);
            return Ok(pipeline);
        }
    }

    /// <inheritdoc />
    public Task<Result<PipelineModel>> GetPipelineAsync(Guid pipelineId, CancellationToken cancellationToken = default)
    {
        lock (gate)
            return pipelines.FirstOrDefault(pipeline => pipeline.Id == pipelineId) is { } pipeline
                ? Ok(pipeline)
                : Fail<PipelineModel>(RegistrationErrors.PipelineNotFound(pipelineId));
    }

    /// <inheritdoc />
    public Task<Result<IReadOnlyList<PipelineModel>>> ListPipelinesAsync(Guid containerId, CancellationToken cancellationToken = default)
    {
        lock (gate)
        {
            if (containers.All(container => container.Id != containerId))
                return Fail<IReadOnlyList<PipelineModel>>(RegistrationErrors.ContainerNotFound(containerId));
            return Ok<IReadOnlyList<PipelineModel>>([.. pipelines.Where(pipeline => pipeline.ContainerId == containerId)]);
        }
    }

    private static Task<Result<T>> Ok<T>(T value) where T : notnull => Task.FromResult(Result<T>.Ok(value));

    private static Task<Result<T>> Fail<T>(AppError error) where T : notnull => Task.FromResult(Result<T>.Fail(error));

    private static string? Name(string? value, int maxLength) =>
        value?.Trim() is { Length: > 0 } trimmed && trimmed.Length <= maxLength ? trimmed : null;

    private static string? Optional(string? value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static string? Handle(SourceFamily family, string? raw)
    {
        var value = raw?.Trim().ToLowerInvariant() ?? string.Empty;
        if (value.Length == 0)
            return null;

        if (WebFamilies.Contains(family))
        {
            var host = value.Contains("://", StringComparison.Ordinal) && Uri.TryCreate(value, UriKind.Absolute, out var uri)
                ? uri.Host
                : value.Split('/', 2)[0].Split(':', 2)[0];
            host = host.TrimEnd('.');
            try
            {
                host = new IdnMapping().GetAscii(host).ToLowerInvariant();
            }
            catch (ArgumentException)
            {
                return null;
            }

            return Uri.CheckHostName(host) == UriHostNameType.Dns && host.Length <= SourceConstants.MaxHandleLength ? host : null;
        }

        if (family == SourceFamily.Telegram)
        {
            foreach (var prefix in new[] { "https://t.me/s/", "https://t.me/", "http://t.me/", "t.me/s/", "t.me/", SourceConstants.TelegramHandlePrefix, "@" })
            {
                if (value.StartsWith(prefix, StringComparison.Ordinal))
                {
                    value = value[prefix.Length..];
                    break;
                }
            }

            value = value.Split('/', 2)[0];
            return TelegramPattern().IsMatch(value) ? SourceConstants.TelegramHandlePrefix + value : null;
        }

        value = value.TrimStart('@');
        return value.Length is > 0 and <= SourceConstants.MaxHandleLength && !value.Any(char.IsWhiteSpace) ? value : null;
    }

    private static string? ScopeKey(PipelineScope? scope)
    {
        var url = scope?.Url?.Trim();
        var parameters = scope?.Parameters ?? new Dictionary<string, string>();
        if (string.IsNullOrEmpty(url) && parameters.Count == 0)
            return PipelineConstants.WholeScopeKey;

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(url))
        {
            if (!Uri.TryCreate(url, UriKind.Absolute, out var uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
                return null;
            var query = string.Join('&', uri.Query.TrimStart('?').Split('&', StringSplitOptions.RemoveEmptyEntries).OrderBy(pair => pair, StringComparer.Ordinal));
            var normalized = uri.Scheme + "://" + uri.IdnHost.ToLowerInvariant()
                + (uri.IsDefaultPort ? string.Empty : ":" + uri.Port.ToString(CultureInfo.InvariantCulture))
                + uri.AbsolutePath + (query.Length > 0 ? "?" + query : string.Empty);
            parts.Add("url=" + Uri.EscapeDataString(normalized));
        }

        foreach (var (name, value) in parameters.OrderBy(pair => pair.Key.Trim(), StringComparer.Ordinal))
        {
            if (string.IsNullOrWhiteSpace(name))
                return null;
            parts.Add(Uri.EscapeDataString(name.Trim()) + "=" + Uri.EscapeDataString(value?.Trim() ?? string.Empty));
        }

        var key = string.Join('&', parts);
        return key.Length <= PipelineConstants.MaxScopeKeyLength ? key : null;
    }

    private static bool IsValid(IReadOnlyList<ScheduleDeclaration> schedules)
    {
        var kinds = new HashSet<WorkKind>();
        return schedules.All(schedule =>
            PipelineConstants.SchedulableKinds.Contains(schedule.Kind)
            && kinds.Add(schedule.Kind)
            && TimeZoneInfo.TryFindSystemTimeZoneById(schedule.TimeZone, out _)
            && (schedule.Window is not { } window || window.Start != window.End)
            && schedule.Mode switch
            {
                ScheduleMode.Interval => schedule.Every >= PipelineConstants.MinInterval && schedule.Cron is null,
                ScheduleMode.Cron => schedule.Every is null && schedule.Cron?.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length is 5 or 6,
                ScheduleMode.Adaptive => schedule.Every >= PipelineConstants.MinInterval && schedule.MaxEvery >= schedule.Every,
                _ => true,
            });
    }

    private static bool IsValid(IReadOnlyDictionary<string, StepOverride> steps) =>
        steps.All(pair => KeyPattern().IsMatch(pair.Key) && IsStep(pair.Value.Lane, pair.Value.MaxUsdPerMonth));

    private static bool IsValid(CapabilityPolicy policy) =>
        (policy.Defaults is null || IsStep(policy.Defaults.Lane, policy.Defaults.MaxUsdPerMonth))
        && policy.Steps.All(pair => KeyPattern().IsMatch(pair.Key) && IsStep(pair.Value.Lane, pair.Value.MaxUsdPerMonth))
        && policy.MonthUsd is not < 0;

    private static bool IsValid(PipelineLimits limits) =>
        limits.MaxPages is null or (>= 1 and <= PipelineConstants.MaxPagesCap)
        && limits.MaxItems is null or (>= 1 and <= PipelineConstants.MaxItemsCap)
        && limits.MaxRequests is not <= 0 && limits.MaxBytes is not <= 0 && limits.MaxProbes is not <= 0
        && (limits.MaxDuration is not { } duration || duration > TimeSpan.Zero);

    private static bool IsStep(string? lane, decimal? maxUsdPerMonth) =>
        (lane is null || KeyPattern().IsMatch(lane)) && maxUsdPerMonth is not < 0;

    [GeneratedRegex("^[a-z0-9](?:[a-z0-9-]*[a-z0-9])?$")]
    private static partial Regex SlugPattern();

    [GeneratedRegex(StepConstants.KeyPattern)]
    private static partial Regex KeyPattern();

    [GeneratedRegex("^[a-z][a-z0-9_]{3,31}$")]
    private static partial Regex TelegramPattern();
}
