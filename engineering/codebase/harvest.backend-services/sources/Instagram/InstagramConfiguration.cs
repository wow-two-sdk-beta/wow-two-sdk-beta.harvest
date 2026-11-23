using System.Text.Json;
using System.Text.RegularExpressions;

namespace WoW.Two.Sdk.Beta.Be.HarvestAdapters.Instagram;

/// <summary>Local validation shared by readiness and execution; performs no provider calls.</summary>
internal static partial class InstagramConfiguration
{
    internal static bool IsValid(InstagramOptions options) => !string.IsNullOrWhiteSpace(options.AccessToken) && options.AccessToken.Length <= 8192
        && options.AccessToken.All(value => value is > (char)32 and < (char)127)
        && Uri.TryCreate(options.BaseAddress, UriKind.Absolute, out var address)
        && address.Scheme == "https" && address.IsDefaultPort && address.IdnHost == "graph.facebook.com"
        && address.UserInfo.Length == 0 && address.AbsolutePath == "/" && address.Query.Length == 0 && address.Fragment.Length == 0
        && NumericId().IsMatch(options.ProfessionalAccountId ?? string.Empty) && Version().IsMatch(options.ApiVersion ?? string.Empty)
        && options.RequestsPerHour is >= 1 and <= InstagramCollectionLimits.MaximumRequestsPerHour;

    internal static string? Text(JsonElement node, string key) => node.ValueKind == JsonValueKind.Object
        && node.TryGetProperty(key, out JsonElement value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    [GeneratedRegex(@"^[a-zA-Z0-9_.]{1,30}$", RegexOptions.CultureInvariant)]
    internal static partial Regex Username();
    [GeneratedRegex(@"^[0-9]{1,32}$", RegexOptions.CultureInvariant)]
    private static partial Regex NumericId();
    [GeneratedRegex(@"^v[0-9]{1,3}\.0$", RegexOptions.CultureInvariant)]
    private static partial Regex Version();
}
