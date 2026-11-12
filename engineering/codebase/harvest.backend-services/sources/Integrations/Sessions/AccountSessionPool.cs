using System.Net;
using Microsoft.Extensions.Options;
using WoW.Two.Sdk.Beta.Be.Harvest.Capabilities.Fetching;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Sessions;

/// <summary>Leases existing HTTPS account sessions to one in-process fetch at a time.</summary>
public sealed class AccountSessionPool(IOptions<HarvestIntegrationOptions> options, TimeProvider clock)
{
    private readonly Lock gate = new();
    private readonly Dictionary<string, Entry> sessions = new(StringComparer.Ordinal);
    private int next;

    internal AccountSessionLease Acquire(Uri origin, IReadOnlyList<string> allowedIds)
    {
        if (origin.Scheme != "https")
            throw new FetchTransportUnavailableException("Account sessions require an HTTPS source origin.");
        lock (gate)
        {
            if (options.Value.Sessions is null || options.Value.Sessions.Any(session => session is null))
                throw new FetchTransportUnavailableException("Account-session settings are missing.");
            if (options.Value.Sessions.Select(session => session.Id).Distinct(StringComparer.Ordinal).Count() != options.Value.Sessions.Count)
                throw new FetchTransportUnavailableException("Account-session identifiers must be unique.");
            List<AccountSessionOptions> candidates = options.Value.Sessions
                .Where(session => allowedIds.Contains(session.Id, StringComparer.Ordinal)
                    && session.ExpiresAtUtc > clock.GetUtcNow()
                    && IntegrationValidation.TryOrigin(session.Origin, out Uri configured, true)
                    && IntegrationValidation.SameOrigin(configured, origin))
                .OrderBy(session => session.Id, StringComparer.Ordinal).ToList();
            for (int offset = 0; offset < candidates.Count; offset++)
            {
                AccountSessionOptions session = candidates[(next + offset) % candidates.Count];
                if (!sessions.TryGetValue(session.Id, out Entry? entry))
                {
                    entry = Create(session, origin);
                    sessions.Add(session.Id, entry);
                }
                if (entry.Busy)
                    continue;
                entry.Busy = true;
                next = (next + offset + 1) % candidates.Count;
                return new AccountSessionLease(entry.Cookies, session.ExpiresAtUtc, () => Release(entry));
            }
            throw new FetchTransportUnavailableException("No unexpired account session is available for this source.");
        }
    }

    private static Entry Create(AccountSessionOptions session, Uri origin)
    {
        if (string.IsNullOrWhiteSpace(session.Id) || session.Id.Length > 80 || session.Cookies is null || session.Cookies.Count is 0 or > 100)
            throw new FetchTransportUnavailableException("Account-session settings are incomplete.");
        var jar = new CookieContainer(100, 100, 16_384);
        try
        {
            foreach (SessionCookieOptions configured in session.Cookies)
            {
                if (configured is null || string.IsNullOrWhiteSpace(configured.Name) || string.IsNullOrEmpty(configured.Value)
                    || string.IsNullOrEmpty(configured.Path) || !configured.Path.StartsWith('/') || configured.Value.Length > 16_384)
                    throw new CookieException();
                jar.Add(origin, new Cookie(configured.Name, configured.Value, configured.Path)
                {
                    Secure = true,
                    HttpOnly = true,
                });
            }
        }
        catch (CookieException)
        {
            throw new FetchTransportUnavailableException("Account-session cookies are invalid.");
        }
        return new Entry(jar);
    }

    private void Release(Entry entry)
    {
        lock (gate)
            entry.Busy = false;
    }

    private sealed class Entry(CookieContainer cookies)
    {
        internal CookieContainer Cookies { get; } = cookies;
        internal bool Busy { get; set; }
    }
}
