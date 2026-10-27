using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Npgsql;
using WoW.Two.Sdk.Beta.Be.Foundation.Errors;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence.Rows;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Tenants;
using WoW.Two.Sdk.Beta.Be.Identity.ApiKeys;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Registration;

/// <summary>Integrates the registry over the module's store: validates, normalizes and writes the registry tables.</summary>
/// <remarks>Duplicates are checked before the insert for a readable error, and the unique indexes catch the race.</remarks>
internal sealed class ModuleRegistrationBroker(HarvestDbContext db, TimeProvider time) : IRegistrationBroker
{
    /// <summary>Holds PostgreSQL's unique-violation state.</summary>
    private const string UniqueViolation = "23505";

    /// <summary>Mints Harvest-shaped secrets with core's factory, independent of any key scheme the host registers.</summary>
    private static readonly ApiKeySecretFactory Secrets = new(Options.Create(new ApiKeyOptions
    {
        Marker = ApiKeyConstants.Marker,
        RandomLength = ApiKeyConstants.RandomLength,
    }));

    /// <inheritdoc />
    public async Task<Result<TenantModel>> RegisterTenantAsync(TenantRegistration registration, CancellationToken cancellationToken = default)
    {
        var (slug, slugError) = RegistrationRules.NormalizeSlug(registration.Slug);
        if (slugError is not null)
            return Result<TenantModel>.Fail(slugError);
        var (name, nameError) = RegistrationRules.NormalizeName(registration.Name);
        if (nameError is not null)
            return Result<TenantModel>.Fail(nameError);
        if (await db.Tenants.AnyAsync(row => row.Slug == slug, cancellationToken))
            return Result<TenantModel>.Fail(RegistrationErrors.TenantSlugTaken(slug!));

        var now = Now();
        var row = new TenantRow
        {
            Id = Guid.CreateVersion7(now),
            Slug = slug!,
            Name = name!,
            State = TenantState.Active,
            PlatformCredentialsAllowed = registration.PlatformCredentialsAllowed,
            CreatedAt = now,
        };
        db.Tenants.Add(row);

        return await SaveAsync(row.ToModel, () => RegistrationErrors.TenantSlugTaken(slug!), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<TenantModel>> GetTenantAsync(Guid tenantId, CancellationToken cancellationToken = default) =>
        await db.Tenants.AsNoTracking().SingleOrDefaultAsync(row => row.Id == tenantId, cancellationToken) is { } row
            ? Result<TenantModel>.Ok(row.ToModel())
            : Result<TenantModel>.Fail(RegistrationErrors.TenantNotFound(tenantId));

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<TenantModel>>> ListTenantsAsync(CancellationToken cancellationToken = default)
    {
        var rows = await db.Tenants.AsNoTracking().OrderBy(row => row.Slug).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<TenantModel>>.Ok([.. rows.Select(row => row.ToModel())]);
    }

    /// <inheritdoc />
    public async Task<Result<IssuedApiKeyModel>> IssueApiKeyAsync(Guid tenantId, ApiKeyIssue issue, CancellationToken cancellationToken = default)
    {
        if (!await db.Tenants.AnyAsync(row => row.Id == tenantId, cancellationToken))
            return Result<IssuedApiKeyModel>.Fail(RegistrationErrors.TenantNotFound(tenantId));

        var now = Now();
        if (RegistrationRules.ValidateApiKeyIssue(issue, now) is { } error)
            return Result<IssuedApiKeyModel>.Fail(error);

        var secret = Secrets.Create();
        var row = new ApiKeyRow
        {
            Id = Guid.CreateVersion7(now),
            TenantId = tenantId,
            Name = issue.Name.Trim(),
            Prefix = secret.Prefix,
            Hash = secret.Hash,
            Scopes = [.. issue.Scopes.Distinct(StringComparer.Ordinal)],
            CreatedAt = now,
            ExpiresAt = issue.ExpiresAtUtc is { } expires ? ToMicroseconds(expires.ToUniversalTime()) : null,
        };
        db.ApiKeys.Add(row);
        await db.SaveChangesAsync(cancellationToken);

        return Result<IssuedApiKeyModel>.Ok(new IssuedApiKeyModel { Key = row.ToModel(), Secret = secret.Secret });
    }

    /// <inheritdoc />
    public async Task<Result<ApiKeyModel>> GetApiKeyAsync(Guid keyId, CancellationToken cancellationToken = default) =>
        await db.ApiKeys.AsNoTracking().SingleOrDefaultAsync(row => row.Id == keyId, cancellationToken) is { } row
            ? Result<ApiKeyModel>.Ok(row.ToModel())
            : Result<ApiKeyModel>.Fail(RegistrationErrors.ApiKeyNotFound(keyId));

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ApiKeyModel>>> ListApiKeysAsync(Guid tenantId, CancellationToken cancellationToken = default)
    {
        if (!await db.Tenants.AnyAsync(row => row.Id == tenantId, cancellationToken))
            return Result<IReadOnlyList<ApiKeyModel>>.Fail(RegistrationErrors.TenantNotFound(tenantId));

        var rows = await db.ApiKeys.AsNoTracking()
            .Where(row => row.TenantId == tenantId)
            .OrderBy(row => row.CreatedAt).ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
        return Result<IReadOnlyList<ApiKeyModel>>.Ok([.. rows.Select(row => row.ToModel())]);
    }

    /// <inheritdoc />
    public async Task<Result<ApiKeyModel>> RevokeApiKeyAsync(Guid tenantId, Guid keyId, CancellationToken cancellationToken = default)
    {
        var row = await db.ApiKeys.SingleOrDefaultAsync(key => key.Id == keyId && key.TenantId == tenantId, cancellationToken);
        if (row is null)
            return Result<ApiKeyModel>.Fail(RegistrationErrors.ApiKeyNotFound(keyId));

        if (row.RevokedAt is null)
        {
            row.RevokedAt = Now();
            await db.SaveChangesAsync(cancellationToken);
        }

        return Result<ApiKeyModel>.Ok(row.ToModel());
    }

    /// <inheritdoc />
    public async Task<Result<ContainerModel>> RegisterContainerAsync(ContainerRegistration registration, CancellationToken cancellationToken = default)
    {
        if (RegistrationRules.ValidateContainerOwner(registration) is { } ownerError)
            return Result<ContainerModel>.Fail(ownerError);
        var (externalRef, refError) = RegistrationRules.NormalizeOptional(registration.ExternalRef, "external reference");
        if (refError is not null)
            return Result<ContainerModel>.Fail(refError);
        var (residency, residencyError) = RegistrationRules.NormalizeOptional(registration.Residency, "residency");
        if (residencyError is not null)
            return Result<ContainerModel>.Fail(residencyError);

        if (registration.TenantId is { } tenantId && !await db.Tenants.AnyAsync(row => row.Id == tenantId, cancellationToken))
            return Result<ContainerModel>.Fail(RegistrationErrors.TenantNotFound(tenantId));
        if (registration.Kind == ContainerKind.Pool && await db.Containers.AnyAsync(row => row.Kind == ContainerKind.Pool, cancellationToken))
            return Result<ContainerModel>.Fail(RegistrationErrors.PoolExists());
        if (externalRef is not null && await db.Containers.AnyAsync(
                row => row.TenantId == registration.TenantId && row.ExternalRef == externalRef, cancellationToken))
            return Result<ContainerModel>.Fail(RegistrationErrors.ContainerRefTaken(externalRef));

        var now = Now();
        var row = new ContainerRow
        {
            Id = Guid.CreateVersion7(now),
            Kind = registration.Kind,
            TenantId = registration.TenantId,
            ExternalRef = externalRef,
            Residency = residency,
            EmbeddingsEnabled = registration.EmbeddingsEnabled,
            State = ContainerState.Active,
            CreatedAt = now,
        };
        db.Containers.Add(row);

        return await SaveAsync(row.ToModel, () => registration.Kind == ContainerKind.Pool
            ? RegistrationErrors.PoolExists()
            : RegistrationErrors.ContainerRefTaken(externalRef ?? string.Empty), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<ContainerModel>> GetContainerAsync(Guid containerId, CancellationToken cancellationToken = default) =>
        await db.Containers.AsNoTracking().SingleOrDefaultAsync(row => row.Id == containerId, cancellationToken) is { } row
            ? Result<ContainerModel>.Ok(row.ToModel())
            : Result<ContainerModel>.Fail(RegistrationErrors.ContainerNotFound(containerId));

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<ContainerModel>>> ListContainersAsync(Guid? tenantId = null, CancellationToken cancellationToken = default)
    {
        if (tenantId is { } id && !await db.Tenants.AnyAsync(row => row.Id == id, cancellationToken))
            return Result<IReadOnlyList<ContainerModel>>.Fail(RegistrationErrors.TenantNotFound(id));

        var query = db.Containers.AsNoTracking();
        if (tenantId is not null)
            query = query.Where(row => row.TenantId == tenantId);
        var rows = await query.OrderBy(row => row.CreatedAt).ThenBy(row => row.Id).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<ContainerModel>>.Ok([.. rows.Select(row => row.ToModel())]);
    }

    /// <inheritdoc />
    public async Task<Result<SourceModel>> RegisterSourceAsync(SourceRegistration registration, CancellationToken cancellationToken = default)
    {
        if (!await db.Containers.AnyAsync(row => row.Id == registration.ContainerId, cancellationToken))
            return Result<SourceModel>.Fail(RegistrationErrors.ContainerNotFound(registration.ContainerId));

        var (handle, handleError) = RegistrationRules.NormalizeHandle(registration.Family, registration.Handle);
        if (handleError is not null)
            return Result<SourceModel>.Fail(handleError);
        var (externalRef, refError) = RegistrationRules.NormalizeOptional(registration.ExternalRef, "external reference");
        if (refError is not null)
            return Result<SourceModel>.Fail(refError);
        if (RegistrationRules.ValidateSource(registration) is { } sourceError)
            return Result<SourceModel>.Fail(sourceError);
        if (await db.Sources.AnyAsync(
                row => row.ContainerId == registration.ContainerId && row.Family == registration.Family && row.Handle == handle,
                cancellationToken))
            return Result<SourceModel>.Fail(RegistrationErrors.SourceExists(handle!));

        Guid? checkPolicyId = registration.CheckPolicy is { } checkPolicy
            ? await db.CheckPolicies.Where(row => row.Key == checkPolicy).Select(row => (Guid?)row.Id).SingleOrDefaultAsync(cancellationToken)
            : null;
        if (registration.CheckPolicy is not null && checkPolicyId is null)
            return Result<SourceModel>.Fail(RegistrationErrors.Invalid($"The check policy '{registration.CheckPolicy}' is not seeded."));

        var now = Now();
        var row = new SourceRow
        {
            Id = Guid.CreateVersion7(now),
            ContainerId = registration.ContainerId,
            Family = registration.Family,
            Handle = handle!,
            ExternalRef = externalRef,
            Coverage = registration.Coverage,
            Completeness = registration.Completeness,
            Posture = registration.Posture,
            Permission = registration.Permission,
            AiAllowed = registration.AiAllowed,
            PoolShareable = registration.PoolShareable,
            RobotsPolicy = registration.RobotsPolicy,
            ConsentBasis = registration.ConsentBasis,
            Authority = (short)registration.Authority,
            RetentionClass = registration.RetentionClass,
            CheckPolicyId = checkPolicyId,
            CapabilityPolicy = StoreJson.Write(registration.CapabilityPolicy ?? new CapabilityPolicy()),
            State = SourceState.Active,
            StateSince = now,
            CreatedAt = now,
        };
        db.Sources.Add(row);

        return await SaveAsync(() => row.ToModel(registration.CheckPolicy), () => RegistrationErrors.SourceExists(handle!), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<SourceModel>> GetSourceAsync(Guid sourceId, CancellationToken cancellationToken = default)
    {
        var row = await db.Sources.AsNoTracking().SingleOrDefaultAsync(source => source.Id == sourceId, cancellationToken);
        if (row is null)
            return Result<SourceModel>.Fail(RegistrationErrors.SourceNotFound(sourceId));

        var checkPolicy = row.CheckPolicyId is { } id
            ? await db.CheckPolicies.AsNoTracking().Where(policy => policy.Id == id).Select(policy => policy.Key).SingleOrDefaultAsync(cancellationToken)
            : null;
        return Result<SourceModel>.Ok(row.ToModel(checkPolicy));
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<SourceModel>>> ListSourcesAsync(Guid containerId, CancellationToken cancellationToken = default)
    {
        if (!await db.Containers.AnyAsync(row => row.Id == containerId, cancellationToken))
            return Result<IReadOnlyList<SourceModel>>.Fail(RegistrationErrors.ContainerNotFound(containerId));

        var keys = await CheckPolicyKeysAsync(cancellationToken);
        var rows = await db.Sources.AsNoTracking().Where(row => row.ContainerId == containerId).ToListAsync(cancellationToken);
        return Result<IReadOnlyList<SourceModel>>.Ok([.. rows
            .OrderBy(row => row.Family).ThenBy(row => row.Handle, StringComparer.Ordinal)
            .Select(row => row.ToModel(row.CheckPolicyId is { } id ? keys.GetValueOrDefault(id) : null))]);
    }

    /// <inheritdoc />
    public async Task<Result<PipelineModel>> DeclarePipelineAsync(PipelineDeclaration declaration, CancellationToken cancellationToken = default)
    {
        if (!await db.Containers.AnyAsync(row => row.Id == declaration.ContainerId, cancellationToken))
            return Result<PipelineModel>.Fail(RegistrationErrors.ContainerNotFound(declaration.ContainerId));
        var source = await db.Sources.AsNoTracking().SingleOrDefaultAsync(row => row.Id == declaration.SourceId, cancellationToken);
        if (source is null)
            return Result<PipelineModel>.Fail(RegistrationErrors.SourceNotFound(declaration.SourceId));
        if (PipelineRules.ValidateSource(declaration, source.ContainerId, source.Permission, source.Coverage) is { } sourceError)
            return Result<PipelineModel>.Fail(sourceError);
        if (RegistrationRules.ValidatePipeline(declaration) is { } pipelineError)
            return Result<PipelineModel>.Fail(pipelineError);
        var (scopeKey, scopeError) = RegistrationRules.CanonicalScopeKey(declaration.Scope);
        if (scopeError is not null)
            return Result<PipelineModel>.Fail(scopeError);
        if (await db.Pipelines.AnyAsync(
                row => row.ContainerId == declaration.ContainerId && row.SourceId == declaration.SourceId && row.ScopeKey == scopeKey,
                cancellationToken))
            return Result<PipelineModel>.Fail(RegistrationErrors.PipelineExists(scopeKey!));

        var now = Now();
        var pipeline = new PipelineRow
        {
            Id = Guid.CreateVersion7(now),
            ContainerId = declaration.ContainerId,
            SourceId = declaration.SourceId,
            ScopeKey = scopeKey!,
            Scope = StoreJson.Write(declaration.Scope ?? new PipelineScope()),
            Name = declaration.Name?.Trim(),
            State = PipelineRules.InitialState(source.Permission),
            StateSince = now,
            Pinned = declaration.Pinned,
            AiMode = declaration.Ai,
            Limits = StoreJson.Write(declaration.Limits),
            StepOverrides = StoreJson.Write(declaration.Steps),
            CreatedAt = now,
        };
        var schedules = declaration.Schedules.Select(schedule => new ScheduleRow
        {
            Id = Guid.CreateVersion7(now),
            ContainerId = declaration.ContainerId,
            PipelineId = pipeline.Id,
            Kind = schedule.Kind,
            Mode = schedule.Mode,
            EveryInterval = schedule.Every,
            MaxInterval = schedule.MaxEvery,
            Cron = schedule.Cron?.Trim(),
            TimeZone = schedule.TimeZone,
            WindowStart = schedule.Window?.Start,
            WindowEnd = schedule.Window?.End,
            Enabled = schedule.Enabled,
            CreatedAt = now,
        }).ToList();
        db.Pipelines.Add(pipeline);
        db.Schedules.AddRange(schedules);

        return await SaveAsync(() => pipeline.ToModel(schedules), () => RegistrationErrors.PipelineExists(scopeKey!), cancellationToken);
    }

    /// <inheritdoc />
    public async Task<Result<PipelineModel>> GetPipelineAsync(Guid pipelineId, CancellationToken cancellationToken = default)
    {
        var row = await db.Pipelines.AsNoTracking().SingleOrDefaultAsync(pipeline => pipeline.Id == pipelineId, cancellationToken);
        if (row is null)
            return Result<PipelineModel>.Fail(RegistrationErrors.PipelineNotFound(pipelineId));

        var schedules = await db.Schedules.AsNoTracking().Where(schedule => schedule.PipelineId == pipelineId).ToListAsync(cancellationToken);
        return Result<PipelineModel>.Ok(row.ToModel(schedules));
    }

    /// <inheritdoc />
    public async Task<Result<IReadOnlyList<PipelineModel>>> ListPipelinesAsync(Guid containerId, CancellationToken cancellationToken = default)
    {
        if (!await db.Containers.AnyAsync(row => row.Id == containerId, cancellationToken))
            return Result<IReadOnlyList<PipelineModel>>.Fail(RegistrationErrors.ContainerNotFound(containerId));

        var rows = await db.Pipelines.AsNoTracking()
            .Where(row => row.ContainerId == containerId)
            .OrderBy(row => row.CreatedAt).ThenBy(row => row.Id)
            .ToListAsync(cancellationToken);
        var ids = rows.Select(row => row.Id).ToList();
        var schedules = (await db.Schedules.AsNoTracking().Where(schedule => ids.Contains(schedule.PipelineId)).ToListAsync(cancellationToken))
            .ToLookup(schedule => schedule.PipelineId);
        return Result<IReadOnlyList<PipelineModel>>.Ok([.. rows.Select(row => row.ToModel(schedules[row.Id]))]);
    }

    /// <summary>Reads the clock at PostgreSQL's microsecond precision, so a written row reads back unchanged.</summary>
    private DateTimeOffset Now() => ToMicroseconds(time.GetUtcNow());

    /// <summary>Drops the ticks below a microsecond, which timestamptz does not keep.</summary>
    private static DateTimeOffset ToMicroseconds(DateTimeOffset value) => value.AddTicks(-(value.Ticks % 10));

    /// <summary>Reads the seeded check policies' keys by identifier.</summary>
    private async Task<Dictionary<Guid, string>> CheckPolicyKeysAsync(CancellationToken cancellationToken) =>
        await db.CheckPolicies.AsNoTracking().ToDictionaryAsync(row => row.Id, row => row.Key, cancellationToken);

    /// <summary>Saves the pending rows, mapping a unique violation that raced the pre-check to <paramref name="conflict"/>.</summary>
    private async Task<Result<T>> SaveAsync<T>(Func<T> model, Func<AppError> conflict, CancellationToken cancellationToken) where T : notnull
    {
        try
        {
            await db.SaveChangesAsync(cancellationToken);
            return Result<T>.Ok(model());
        }
        catch (DbUpdateException exception) when (exception.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            db.ChangeTracker.Clear();
            return Result<T>.Fail(conflict());
        }
    }
}
