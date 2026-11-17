namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;

/// <summary>Validates professional publisher names and the profile scopes saved in the registry.</summary>
public static class InstagramPublishers
{
    /// <summary>Identifies the domain handles used by legacy multi-publisher sources.</summary>
    public static bool IsLegacyDomain(string handle) => handle is "www.instagram.com" or "instagram.com";

    /// <summary>Resolves a publisher source's username and public entry URL independently of the provider transport.</summary>
    public static bool TryReadSourceProfile(string handle, out string username, out string profile)
    {
        username = profile = string.Empty;
        if (IsLegacyDomain(handle) || Normalize([handle]) is not { Count: 1 } names) return false;
        username = names[0];
        profile = $"https://www.instagram.com/{username}/";
        return true;
    }

    /// <summary>Checks a canonical profile against its publisher identity, retaining legacy domain-source compatibility.</summary>
    public static bool MatchesSource(string handle, string profile) => IsLegacyDomain(handle)
        ? new Uri(profile).IdnHost == handle
        : TryReadSourceProfile(handle, out _, out string sourceProfile) && profile == sourceProfile;

    /// <summary>Returns trimmed, lowercase, unique usernames in stable order; any invalid or reserved name rejects the set.</summary>
    public static IReadOnlyList<string> Normalize(IEnumerable<string>? usernames)
    {
        if (usernames is null)
            return [];
        var normalized = new SortedSet<string>(StringComparer.Ordinal);
        foreach (string? raw in usernames)
        {
            string? username = raw?.Trim().ToLowerInvariant();
            if (username is null || !InstagramConfiguration.Username().IsMatch(username)
                || username is "p" or "reel" or "reels" or "tv" or "explore" or "stories" or "accounts" or "direct" or "about" or "developer")
                return [];
            normalized.Add(username);
        }
        return [.. normalized];
    }

    /// <summary>Reads one public profile URL, without query overrides, post paths or credentials.</summary>
    public static bool TryReadProfile(string? value, out string username, out string profile)
    {
        username = profile = string.Empty;
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri) || uri.Scheme != "https" || !uri.IsDefaultPort
            || uri.UserInfo.Length > 0 || uri.Query.Length > 0 || uri.Fragment.Length > 0
            || uri.IdnHost is not ("www.instagram.com" or "instagram.com")) return false;
        string[] segments = uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length != 1 || Normalize(segments) is not { Count: 1 } names) return false;
        username = names[0];
        profile = $"https://{uri.IdnHost}/{username}/";
        return true;
    }
}
