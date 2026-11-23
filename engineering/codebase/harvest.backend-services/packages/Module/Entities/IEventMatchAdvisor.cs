namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Entities;

/// <summary>Uses a post's evidence and a scoped read-only lookup to resolve an uncertain event identity.</summary>
public interface IEventMatchAdvisor
{
    /// <summary>Advises on series identity. The resolver owns occurrence matching and validates the decision before writing.</summary>
    Task<EventMatchDecision?> AdviseAsync(EventMatchEvidence evidence,
        Func<string?, CancellationToken, Task<IReadOnlyList<EventMatchCandidate>>> lookup, CancellationToken cancellationToken);
}

/// <summary>Source evidence for one organized occasion as extracted from the post.</summary>
public sealed record EventMatchEvidence(string? Name, string? OriginalName, string? Organizer, string? Venue,
    string? City, string? Format, string? Edition, string? RegistrationUrl, string Role, string? StartsOn, string? EndsOn, string Text);

/// <summary>An existing series and the closest recorded occurrence returned by a scoped lookup.</summary>
public sealed record EventMatchCandidate(Guid EntityId, string Name, IReadOnlyList<string> Aliases,
    string? Organizer, string? Venue, string? City, string? Format, string? StartsOn, string? EndsOn, string Evidence);

/// <summary>The advisor's choice to link, create, or require review.</summary>
public sealed record EventMatchDecision(string Decision, Guid? EntityId);

internal sealed record EventMatchSuggestion(EventMatchDecision Decision, byte[] ContentHash,
    DateTimeOffset AnswerUpdatedAt, DateTimeOffset? EntityUpdatedAt);
