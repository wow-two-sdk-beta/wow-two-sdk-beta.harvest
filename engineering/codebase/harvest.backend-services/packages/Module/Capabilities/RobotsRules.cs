using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities;

/// <summary>Evaluates the matching robots groups with longest-path and allow-on-tie precedence.</summary>
internal sealed class RobotsRules
{
    private readonly IReadOnlyList<Rule> _rules;

    private RobotsRules(IReadOnlyList<Rule> rules, TimeSpan? delay)
    {
        _rules = rules;
        CrawlDelay = delay;
    }

    internal TimeSpan? CrawlDelay { get; }

    internal bool Allows(Uri uri)
    {
        string path = NormalizePath(uri.GetComponents(UriComponents.PathAndQuery, UriFormat.UriEscaped));
        if (!path.StartsWith('/'))
            path = "/" + path;
        var matched = _rules.Where(rule => rule.Matches(path)).OrderByDescending(rule => rule.Length).ThenByDescending(rule => rule.Allow).FirstOrDefault();
        return matched is null || matched.Allow;
    }

    internal static RobotsRules Parse(string text, string userAgent)
    {
        var groups = new List<Group>();
        Group? group = null;
        bool sawDirective = false;
        foreach (string raw in text.Split('\n'))
        {
            string line = raw.Split('#', 2)[0].Trim().TrimStart('\uFEFF');
            int colon = line.IndexOf(':');
            if (colon <= 0)
                continue;
            string key = line[..colon].Trim();
            string value = line[(colon + 1)..].Trim();
            if (key.Equals("user-agent", StringComparison.OrdinalIgnoreCase))
            {
                if (group is null || sawDirective)
                {
                    group = new Group();
                    groups.Add(group);
                    sawDirective = false;
                }
                group.Agents.Add(value);
                continue;
            }
            if (group is null)
                continue;
            sawDirective = true;
            if (key.Equals("allow", StringComparison.OrdinalIgnoreCase) || key.Equals("disallow", StringComparison.OrdinalIgnoreCase))
            {
                if (value.Length > 0 && value.StartsWith('/'))
                    group.Rules.Add(new Rule(NormalizePath(value), key.Equals("allow", StringComparison.OrdinalIgnoreCase)));
            }
            else if (key.Equals("crawl-delay", StringComparison.OrdinalIgnoreCase)
                && double.TryParse(value, NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out double delay)
                && double.IsFinite(delay) && delay >= 0)
            {
                group.Delay = TimeSpan.FromSeconds(Math.Min(delay, 30));
            }
        }

        string product = userAgent.Split('/', ' ', '(')[0];
        var matches = groups.Select(item => (Group: item, Specificity: item.Agents
            .Select(agent => agent == "*" ? 0 : product.Contains(agent, StringComparison.OrdinalIgnoreCase) && agent.Length > 0 ? agent.Length : -1)
            .DefaultIfEmpty(-1).Max())).Where(item => item.Specificity >= 0).ToArray();
        int best = matches.Select(item => item.Specificity).DefaultIfEmpty(-1).Max();
        var selected = matches.Where(item => item.Specificity == best).Select(item => item.Group).ToArray();
        return new RobotsRules(selected.SelectMany(item => item.Rules).ToArray(),
            selected.Where(item => item.Delay is not null).Select(item => item.Delay).OrderByDescending(delay => delay).FirstOrDefault());
    }

    private static string NormalizePath(string value)
    {
        var builder = new StringBuilder(value.Length);
        for (int index = 0; index < value.Length; index++)
        {
            if (value[index] == '%' && index + 2 < value.Length
                && byte.TryParse(value.AsSpan(index + 1, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out byte octet))
            {
                char character = (char)octet;
                bool unreserved = char.IsAsciiLetterOrDigit(character) || character is '-' or '.' or '_' or '~';
                builder.Append(unreserved ? character.ToString() : "%" + octet.ToString("X2", CultureInfo.InvariantCulture));
                index += 2;
            }
            else if (value[index] > 127)
            {
                int length = char.IsHighSurrogate(value[index]) && index + 1 < value.Length && char.IsLowSurrogate(value[index + 1]) ? 2 : 1;
                foreach (byte octetValue in Encoding.UTF8.GetBytes(value.AsSpan(index, length).ToString()))
                    builder.Append('%').Append(octetValue.ToString("X2", CultureInfo.InvariantCulture));
                index += length - 1;
            }
            else
                builder.Append(value[index]);
        }
        return builder.ToString();
    }

    private sealed class Group
    {
        internal List<string> Agents { get; } = [];
        internal List<Rule> Rules { get; } = [];
        internal TimeSpan? Delay { get; set; }
    }

    private sealed class Rule(string pattern, bool allow)
    {
        private readonly Regex _pattern = new("^" + Regex.Escape(pattern.TrimEnd('$')).Replace("\\*", ".*")
            + (pattern.EndsWith('$') ? "$" : string.Empty), RegexOptions.CultureInvariant | RegexOptions.NonBacktracking, TimeSpan.FromMilliseconds(50));

        internal bool Allow { get; } = allow;
        internal int Length { get; } = pattern.Count(character => character is not '*' and not '$');
        internal bool Matches(string path) => _pattern.IsMatch(path);
    }
}
