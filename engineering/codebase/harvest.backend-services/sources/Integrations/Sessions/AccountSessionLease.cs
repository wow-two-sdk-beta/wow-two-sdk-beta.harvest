using System.Net;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations.Sessions;

/// <summary>Returns one in-process account slot when a fetch finishes.</summary>
internal sealed class AccountSessionLease(CookieContainer cookies, DateTimeOffset expiresAtUtc, Action release) : IDisposable
{
    private Action? release = release;
    internal CookieContainer Cookies { get; } = cookies;
    internal DateTimeOffset ExpiresAtUtc { get; } = expiresAtUtc;

    public void Dispose()
    {
        Interlocked.Exchange(ref release, null)?.Invoke();
    }
}
