using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence;
using WoW.Two.Sdk.Beta.Be.Harvest.Module.Persistence.Rows;
using WoW.Two.Sdk.Beta.Be.Harvest.Pipelines;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Registration;

/// <summary>Registers the adapter's sections as dormant pipelines on source creation and backfills existing sources.</summary>
internal sealed class SourceSectionRegistration(HarvestDbContext db, IEnumerable<ISourceAdapter> adapters, TimeProvider clock)
{
    internal async Task ReconcileAsync(CancellationToken ct)
    {
        if (!adapters.Any(adapter => adapter is ISourceSectionCatalog))
            return;
        var sources = await db.Sources.AsNoTracking().Where(row => row.State == SourceState.Active
            && row.Permission != SourcePermission.Denied).ToListAsync(ct);
        foreach (SourceRow row in sources)
        {
            if (PassiveSections(row.ToModel(null)).Count == 0)
                continue;
            await using var transaction = await db.Database.BeginTransactionAsync(ct);
            SourceRow? current = await db.Sources.FromSqlInterpolated(
                $"SELECT * FROM harvest_sources WHERE id = {row.Id} FOR UPDATE").AsNoTracking().SingleOrDefaultAsync(ct);
            if (current is { State: SourceState.Active })
                await StageAsync(current.ToModel(null), ct);
            await db.SaveChangesAsync(ct);
            await transaction.CommitAsync(ct);
            db.ChangeTracker.Clear();
        }
    }

    internal async Task StageAsync(SourceModel source, CancellationToken ct)
    {
        if (source.Permission == SourcePermission.Denied)
            return;
        var sections = PassiveSections(source);
        if (sections.Count == 0)
            return;
        var scopes = await db.Pipelines.AsNoTracking().Where(row => row.SourceId == source.Id)
            .Select(row => row.Scope).ToListAsync(ct);
        var urls = scopes.Select(json => StoreJson.Read(json, new PipelineScope()).Url)
            .Where(url => url is not null).Select(url => url!.TrimEnd('/')).ToHashSet(StringComparer.Ordinal);
        DateTimeOffset now = clock.GetUtcNow();
        foreach (SourceSectionDefinition section in sections)
        {
            if (!Uri.TryCreate(section.Url, UriKind.Absolute, out Uri? url) || url.Scheme != Uri.UriSchemeHttps
                || !url.IsDefaultPort || url.UserInfo.Length != 0
                || !string.Equals(url.IdnHost, source.Handle, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The adapter's section must be on the registered source host.");
            if (!urls.Add(section.Url.TrimEnd('/')))
                continue;
            var scope = new PipelineScope { Url = section.Url, RestrictToPath = section.RestrictToPath };
            var (key, error) = RegistrationRules.CanonicalScopeKey(scope);
            if (error is not null)
                throw new InvalidOperationException(error.Message);
            db.Pipelines.Add(new PipelineRow
            {
                Id = Guid.CreateVersion7(now), ContainerId = source.ContainerId, SourceId = source.Id,
                Name = $"{section.Label} ({section.Locale})", ScopeKey = key!, Scope = StoreJson.Write(scope),
                State = PipelineRules.InitialState(source.Permission), StateSince = now, Pinned = false, AiMode = AiMode.Off,
                Limits = StoreJson.Write(new PipelineLimits
                {
                    MaxPages = 5, MaxItems = 150, MaxRequests = 180, MaxBytes = 30_000_000,
                    MaxDuration = TimeSpan.FromMinutes(10), MaxProbes = 100,
                }),
                StepOverrides = "{}", CreatedAt = now,
            });
        }
    }

    private IReadOnlyList<SourceSectionDefinition> PassiveSections(SourceModel source) =>
        adapters.LastOrDefault(adapter => adapter.Serves(source)) is ISourceSectionCatalog catalog
            ? catalog.Sections(source).Where(section => section.RegisterPassivePipeline).ToArray() : [];
}

/// <summary>Reconciles registered source sections after the Harvest schema is ready.</summary>
public static class SourceSectionStartupExtensions
{
    /// <summary>Creates missing passive sections without changing existing pipelines or subscriptions.</summary>
    public static async Task ReconcileHarvestSourceSectionsAsync(this IServiceProvider services, CancellationToken ct)
    {
        await using AsyncServiceScope scope = services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SourceSectionRegistration>().ReconcileAsync(ct);
    }
}
