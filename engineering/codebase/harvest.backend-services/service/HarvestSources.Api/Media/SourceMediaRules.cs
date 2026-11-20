using Microsoft.Extensions.DependencyInjection;
using WoW.Two.Sdk.Beta.Be.Foundation.Results;
using WoW.Two.Sdk.Beta.Be.Harvest.Containers;
using WoW.Two.Sdk.Beta.Be.Harvest.Registration;
using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace HarvestSources.Api.Media;

/// <summary>Resolves which image hosts each active web source's adapter declares, for the queue scan and the copy worker.</summary>
/// <remarks>The adapter is the one run planning picks (the last installed one that serves the source), read through the
/// registry broker, so no schema holds the declaration. The answer is kept for one queue scan interval, so a scan,
/// the claims after it and their copies share one registry read.</remarks>
internal sealed class SourceMediaRules(IServiceScopeFactory scopes, IEnumerable<ISourceAdapter> adapters, TimeProvider clock)
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromMinutes(1);
    private readonly SemaphoreSlim gate = new(1, 1);
    private IReadOnlyDictionary<Guid, SourceMediaRule> rules = new Dictionary<Guid, SourceMediaRule>();
    private DateTimeOffset expiresAt = DateTimeOffset.MinValue;

    /// <summary>Lists the sources whose adapter declares media hosts.</summary>
    public async Task<IReadOnlyCollection<SourceMediaRule>> ListAsync(CancellationToken ct) => (await LoadAsync(ct)).Values.ToArray();

    /// <summary>Finds a source's declared media hosts; null when its adapter declares none.</summary>
    public async Task<SourceMediaRule?> FindAsync(Guid sourceId, CancellationToken ct) =>
        (await LoadAsync(ct)).GetValueOrDefault(sourceId);

    private async Task<IReadOnlyDictionary<Guid, SourceMediaRule>> LoadAsync(CancellationToken ct)
    {
        if (clock.GetUtcNow() < expiresAt)
            return rules;
        await gate.WaitAsync(ct);
        try
        {
            if (clock.GetUtcNow() < expiresAt)
                return rules;
            rules = await ResolveAsync(ct);
            expiresAt = clock.GetUtcNow() + Lifetime;
            return rules;
        }
        finally
        {
            gate.Release();
        }
    }

    private async Task<IReadOnlyDictionary<Guid, SourceMediaRule>> ResolveAsync(CancellationToken ct)
    {
        using var scope = scopes.CreateScope();
        var registry = scope.ServiceProvider.GetRequiredService<IRegistrationBroker>();
        if (await registry.ListContainersAsync(null, ct) is not Result<IReadOnlyList<ContainerModel>>.Success { Value: var containers })
            throw new InvalidOperationException("The source media rules could not list the containers.");
        var resolved = new Dictionary<Guid, SourceMediaRule>();
        foreach (var container in containers)
        {
            if (await registry.ListSourcesAsync(container.Id, ct) is not Result<IReadOnlyList<SourceModel>>.Success { Value: var sources })
                throw new InvalidOperationException($"The source media rules could not list container {container.Id}'s sources.");
            foreach (var source in sources.Where(Copyable))
            {
                if (adapters.LastOrDefault(candidate => candidate.Serves(source))?.Describe(source).Media is not { } media)
                    continue;
                string[] domains = [.. media.Domains.Select(Domain).OfType<string>().Distinct()];
                if (domains.Length > 0)
                    resolved[source.Id] = new(source.Id, domains, Math.Clamp(media.Limit, 0, SourceConstants.MaxPostImages), media.MinDelay);
            }
        }
        return resolved;
    }

    /// <summary>Keeps a declared domain only when it is a dotted host name; the queue's pattern and the destination policy
    /// compare it in lower case.</summary>
    private static string? Domain(string value) => value.Trim().Trim('.').ToLowerInvariant() is { } domain
        && domain.Contains('.') && domain.All(letter => char.IsAsciiLetterOrDigit(letter) || letter is '.' or '-') ? domain : null;

    /// <summary>The sources the queue's web branch covers: an active, permitted own-fetch source of a site-like family.</summary>
    private static bool Copyable(SourceModel source) => source.State == SourceState.Active
        && source.Permission is SourcePermission.Open or SourcePermission.Granted
        && source.Posture == SourcePosture.OwnFetch
        && source.Family is SourceFamily.Site or SourceFamily.Page or SourceFamily.Structured or SourceFamily.Render
            or SourceFamily.Feed or SourceFamily.Listing;
}

/// <summary>Holds one source's declared image hosts: where its post images may be copied from, how many per post, and the
/// least gap between requests to one host.</summary>
internal sealed record SourceMediaRule(Guid SourceId, IReadOnlyList<string> Domains, int Limit, TimeSpan MinDelay);
