using WoW.Two.Sdk.Beta.Be.Harvest.Sources;
using WoW.Two.Sdk.Beta.Be.Harvest.Steps;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Sources;

/// <summary>Separates a registry identity from the adapter's request origin and shared host budget.</summary>
internal sealed record SourceAddress(string HostKey, Uri? Origin)
{
    internal static SourceAddress? Resolve(SourceModel source, AdapterDescriptor? descriptor)
    {
        Uri? origin = null;
        if (descriptor?.SourceUrl is { } declared
            && (declared != declared.Trim() || !Uri.TryCreate(declared, UriKind.Absolute, out origin)
                || origin.Scheme is not ("http" or "https") || origin.UserInfo.Length != 0
                || origin.Host.Length == 0 || origin.Query.Length != 0 || origin.Fragment.Length != 0))
            return null;

        string host = origin?.IdnHost ?? source.Handle;
        if (descriptor?.HostKey is { } key && !string.Equals(key, host, StringComparison.OrdinalIgnoreCase))
            return null;
        return new(host.ToLowerInvariant(), origin);
    }

    internal bool IncludesOrigin(Uri target) =>
        target.Scheme is "http" or "https" && target.UserInfo.Length == 0
        && string.Equals(target.IdnHost, HostKey, StringComparison.OrdinalIgnoreCase)
        && (Origin is null || target.Scheme == Origin.Scheme && target.Port == Origin.Port);
}
