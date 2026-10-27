using System.Text.RegularExpressions;

namespace WoW.Two.Sdk.Beta.Be.Harvest.Module.Capabilities.Robots;

/// <summary>A parsed <c>robots.txt</c>: the groups of rules, matched to a user agent to tell an allowed path from a
/// disallowed one and to read the host's crawl delay.</summary>
/// <remarks>A disallowed path is an outcome, never fetched. This reads a host's own stated wishes; it never works around
/// them.</remarks>
internal sealed class RobotsFile
{
    private readonly IReadOnlyList<Group> groups;

    private RobotsFile(IReadOnlyList<Group> groups, IReadOnlyList<string> sitemaps)
    {
        this.groups = groups;
        Sitemaps = sitemaps;
    }

    /// <summary>A <c>robots.txt</c> that allows everything — what a missing or unreadable file means.</summary>
    public static RobotsFile AllowAll { get; } = new([], []);

    /// <summary>Gets the sitemap URLs the file advertised.</summary>
    public IReadOnlyList<string> Sitemaps { get; }

    /// <summary>Parses a <c>robots.txt</c> body into groups, keeping order so the most specific rule can win.</summary>
    /// <param name="content">The file's text.</param>
    public static RobotsFile Parse(string content)
    {
        ArgumentNullException.ThrowIfNull(content);

        var groups = new List<Group>();
        var sitemaps = new List<string>();
        Group? current = null;
        var sawRule = false;

        foreach (var raw in content.Split('\n'))
        {
            var line = Strip(raw);
            if (line.Length == 0)
                continue;

            var colon = line.IndexOf(':', StringComparison.Ordinal);
            if (colon <= 0)
                continue;
            var field = line[..colon].Trim().ToLowerInvariant();
            var value = line[(colon + 1)..].Trim();

            switch (field)
            {
                case "user-agent":
                    // A user-agent line after a rule starts a fresh group; consecutive ones share the group.
                    if (current is null || sawRule)
                    {
                        current = new Group();
                        groups.Add(current);
                        sawRule = false;
                    }

                    if (value.Length > 0)
                        current.Agents.Add(value.ToLowerInvariant());
                    break;

                case "disallow":
                    current ??= StartGroup(groups);
                    sawRule = true;
                    if (value.Length > 0)
                        current.Rules.Add(new Rule(allow: false, value));
                    break;

                case "allow":
                    current ??= StartGroup(groups);
                    sawRule = true;
                    if (value.Length > 0)
                        current.Rules.Add(new Rule(allow: true, value));
                    break;

                case "crawl-delay":
                    current ??= StartGroup(groups);
                    sawRule = true;
                    if (double.TryParse(value, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var seconds) && seconds >= 0)
                        current.CrawlDelaySeconds = seconds;
                    break;

                case "sitemap":
                    if (value.Length > 0)
                        sitemaps.Add(value);
                    break;
            }
        }

        return new RobotsFile(groups, sitemaps);
    }

    /// <summary>Tells whether <paramref name="pathAndQuery"/> may be fetched by <paramref name="agentToken"/>; the most
    /// specific rule in the best-matching group wins, and an allow ties.</summary>
    /// <param name="pathAndQuery">The request path with its query, such as <c>/ru/events?x=1</c>.</param>
    /// <param name="agentToken">Our product token, such as <c>harvestbot</c>.</param>
    public bool IsAllowed(string pathAndQuery, string agentToken)
    {
        var group = Select(agentToken);
        if (group is null)
            return true;

        Rule? best = null;
        var bestLength = -1;
        foreach (var rule in group.Rules)
            if (rule.Pattern.IsMatch(pathAndQuery) && (rule.Length > bestLength || (rule.Length == bestLength && rule.Allow)))
            {
                best = rule;
                bestLength = rule.Length;
            }

        return best is null || best.Allow;
    }

    /// <summary>Reads the crawl delay the best-matching group states, or null when it states none.</summary>
    /// <param name="agentToken">Our product token.</param>
    public TimeSpan? CrawlDelay(string agentToken) =>
        Select(agentToken)?.CrawlDelaySeconds is { } seconds ? TimeSpan.FromSeconds(seconds) : null;

    /// <summary>Picks the group whose agent best matches ours — the longest named match, else the <c>*</c> group.</summary>
    private Group? Select(string agentToken)
    {
        var token = agentToken.ToLowerInvariant();
        Group? named = null;
        var namedLength = 0;
        Group? wildcard = null;

        foreach (var group in groups)
            foreach (var agent in group.Agents)
                if (agent == "*")
                {
                    wildcard ??= group;
                }
                else if (token.StartsWith(agent, StringComparison.Ordinal) && agent.Length > namedLength)
                {
                    named = group;
                    namedLength = agent.Length;
                }

        return named ?? wildcard;
    }

    private static Group StartGroup(List<Group> groups)
    {
        var group = new Group();
        groups.Add(group);
        return group;
    }

    /// <summary>Drops a comment and trims a line.</summary>
    private static string Strip(string raw)
    {
        var hash = raw.IndexOf('#', StringComparison.Ordinal);
        return (hash >= 0 ? raw[..hash] : raw).Trim();
    }

    /// <summary>One <c>robots.txt</c> group: the agents it names and the rules and crawl delay they share.</summary>
    private sealed class Group
    {
        public List<string> Agents { get; } = [];

        public List<Rule> Rules { get; } = [];

        public double? CrawlDelaySeconds { get; set; }
    }

    /// <summary>One allow or disallow rule, with its path pattern compiled for matching.</summary>
    private sealed class Rule
    {
        public Rule(bool allow, string pattern)
        {
            Allow = allow;
            Length = pattern.Length;
            Pattern = Compile(pattern);
        }

        public bool Allow { get; }

        public int Length { get; }

        public Regex Pattern { get; }

        /// <summary>Compiles a robots path pattern — anchored at the start, <c>*</c> matching any run and a trailing
        /// <c>$</c> anchoring the end — without backtracking.</summary>
        private static Regex Compile(string pattern)
        {
            var anchored = pattern.EndsWith('$');
            var body = anchored ? pattern[..^1] : pattern;
            var builder = new System.Text.StringBuilder("^");
            foreach (var character in body)
                builder.Append(character == '*' ? ".*" : Regex.Escape(character.ToString()));
            if (anchored)
                builder.Append('$');
            return new Regex(builder.ToString(), RegexOptions.NonBacktracking | RegexOptions.CultureInvariant);
        }
    }
}
