namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Integrations;

/// <summary>Keeps private provider origins exact and credential-free.</summary>
internal static class IntegrationValidation
{
    internal static bool IsWeb(Uri? uri)
    {
        return uri is { IsAbsoluteUri: true } && uri.Scheme is "https" or "http"
            && uri.UserInfo.Length == 0 && uri.Host.Length > 0;
    }

    internal static bool TryOrigin(string value, out Uri origin, bool requireHttps = false)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) && IsWeb(uri)
            && (!requireHttps || uri.Scheme == "https") && uri.AbsolutePath == "/"
            && uri.Query.Length == 0 && uri.Fragment.Length == 0)
        {
            origin = uri;
            return true;
        }
        origin = null!;
        return false;
    }

    internal static string OriginKey(Uri uri)
    {
        return new UriBuilder(uri.Scheme, uri.IdnHost.TrimEnd('.').ToLowerInvariant(), uri.IsDefaultPort ? -1 : uri.Port)
            .Uri.GetLeftPart(UriPartial.Authority);
    }

    internal static bool SameOrigin(Uri expected, Uri actual)
    {
        return IsWeb(actual) && string.Equals(OriginKey(expected), OriginKey(actual), StringComparison.Ordinal);
    }

    internal static bool TryProviderBase(string value, out Uri uri)
    {
        if (Uri.TryCreate(value, UriKind.Absolute, out Uri? parsed) && IsWeb(parsed) && parsed.Scheme == "https"
            && parsed.Query.Length == 0 && parsed.Fragment.Length == 0)
        {
            uri = new Uri(parsed.AbsoluteUri.TrimEnd('/') + "/");
            return true;
        }
        uri = null!;
        return false;
    }

    internal static Uri RelativeEndpoint(Uri root, string relative)
    {
        if (string.IsNullOrWhiteSpace(relative) || relative.StartsWith('/') || relative.Contains('\\')
            || !Uri.TryCreate(relative, UriKind.Relative, out _) || relative.Split('/').Contains(".."))
            throw new InvalidOperationException("Provider endpoint paths must stay below their configured base.");
        Uri endpoint = new(root, relative);
        if (!SameOrigin(root, endpoint) || !endpoint.AbsolutePath.StartsWith(root.AbsolutePath, StringComparison.Ordinal)
            || endpoint.Query.Length != 0 || endpoint.Fragment.Length != 0)
            throw new InvalidOperationException("Provider endpoint paths must stay below their configured base.");
        return endpoint;
    }

    internal static string? SafeCode(string? value)
    {
        return value is { Length: > 0 and <= 80 }
            && value.All(character => char.IsAsciiLetterOrDigit(character) || character == '_') ? value : null;
    }
}
