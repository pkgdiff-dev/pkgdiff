using System.Text.RegularExpressions;
using PkgDiff.Application.Interfaces;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Infrastructure.Services;

public class AnalyzerService : IAnalyzerService
{
    // Ordered most specific first
    private static readonly (string[] Prefixes, ChangeCategory Category)[] LinePrefixRules =
    [
        (["breaking change", "breaking"], ChangeCategory.BreakingChange),
        (["new feature", "feature", "added", "feat"], ChangeCategory.NewFeature),
        (["security fix", "security"], ChangeCategory.Security),
        (["bug fix", "bugfix", "fixed", "fix", "bug"], ChangeCategory.BugFix),
        (["performance", "perf", "optimization"], ChangeCategory.Performance),
        (["deprecated", "deprecation", "removal"], ChangeCategory.Deprecation),
        (["change", "changed", "update", "updated", "improvement", "enhanced", "enhancement"], ChangeCategory.Other),
    ];

    private static readonly (string[] Keywords, ChangeCategory Category)[] KeywordRules =
    [
        (["breaking", "incompatible"], ChangeCategory.BreakingChange),
        (["security", "vulnerability", "cve"], ChangeCategory.Security),
        (["deprecat", "obsolete"], ChangeCategory.Deprecation),
        (["performance", "optimize", "faster"], ChangeCategory.Performance),
    ];

    public List<ReleaseNoteEntry> ParseReleaseNotes(string releaseNotes)
    {
        if (string.IsNullOrWhiteSpace(releaseNotes))
            return [];

        var entries = new List<ReleaseNoteEntry>();
        var lines = releaseNotes.Split('\n', StringSplitOptions.RemoveEmptyEntries);

        foreach (var rawLine in lines)
        {
            var line = rawLine.Trim();
            if (string.IsNullOrWhiteSpace(line))
                continue;

            var cleaned = StripListMarker(line);

            var (category, text) = DetectAndStripPrefix(cleaned);

            if (string.IsNullOrWhiteSpace(text))
                continue;

            entries.Add(new ReleaseNoteEntry
            {
                Category = category,
                Text = text.Trim()
            });
        }

        return entries;
    }

    public List<ChangeCategory> CategorizeReleaseNotes(string releaseNotes)
    {
        if (string.IsNullOrWhiteSpace(releaseNotes))
            return [ChangeCategory.Other];

        var entries = ParseReleaseNotes(releaseNotes);
        var categories = entries
            .Select(e => e.Category)
            .Where(c => c != ChangeCategory.Other)
            .ToHashSet();

        if (categories.Count == 0)
        {
            var lower = releaseNotes.ToLowerInvariant();
            foreach (var (keywords, category) in KeywordRules)
            {
                if (keywords.Any(k => lower.Contains(k)))
                    categories.Add(category);
            }
        }

        return categories.Count > 0
            ? categories.ToList()
            : [ChangeCategory.Other];
    }

    public Summary GenerateSummary(List<VersionInfo> versions)
    {
        var summary = new Summary();

        foreach (var version in versions)
        {
            foreach (var category in version.Categories)
            {
                switch (category)
                {
                    case ChangeCategory.BreakingChange:
                        summary.BreakingChanges++;
                        break;
                    case ChangeCategory.BugFix:
                        summary.BugFixes++;
                        break;
                    case ChangeCategory.NewFeature:
                        summary.NewFeatures++;
                        break;
                    case ChangeCategory.Performance:
                        summary.PerformanceImprovements++;
                        break;
                    case ChangeCategory.Security:
                        summary.SecurityFixes++;
                        break;
                    case ChangeCategory.Deprecation:
                        summary.Deprecations++;
                        break;
                    case ChangeCategory.Other:
                        summary.OtherChanges++;
                        break;
                }
            }
        }

        return summary;
    }

    private static (ChangeCategory Category, string Text) DetectAndStripPrefix(string line)
    {
        var lower = line.ToLowerInvariant();

        foreach (var (prefixes, category) in LinePrefixRules)
        {
            foreach (var prefix in prefixes)
            {
                if (!lower.StartsWith(prefix))
                    continue;

                var rest = line[prefix.Length..].TrimStart();

                if (rest.Length == 0)
                    return (category, string.Empty);

                if (rest[0] is '-' or ':' or '–' or '—')
                {
                    var text = rest[1..].TrimStart();
                    return (category, text);
                }

                if (rest[0] is ' ' or '#')
                    return (category, rest.TrimStart());
            }
        }

        if (lower.StartsWith('#'))
        {
            var heading = line.TrimStart('#').Trim();
            var headingLower = heading.ToLowerInvariant();

            foreach (var (prefixes, category) in LinePrefixRules)
            {
                if (prefixes.Any(p => headingLower.StartsWith(p)))
                    return (category, string.Empty);
            }
        }

        return (ChangeCategory.Other, line);
    }

    private static string StripListMarker(string line)
    {
        var trimmed = line.TrimStart();

        if (trimmed.Length > 1 && trimmed[0] is '-' or '*' or '•' && trimmed[1] == ' ')
            return trimmed[2..].TrimStart();

        var i = 0;
        while (i < trimmed.Length && char.IsDigit(trimmed[i])) i++;
        if (i > 0 && i < trimmed.Length && trimmed[i] is '.' or ')')
        {
            var after = trimmed[(i + 1)..].TrimStart();
            if (after.Length > 0)
                return after;
        }

        return trimmed;
    }

    private static readonly HashSet<string> _mentionBlocklist = new(StringComparer.OrdinalIgnoreCase)
    {
        "param", "returns", "return", "deprecated", "since", "see", "throws", "throw",
        "type", "typedef", "property", "prop", "readonly", "example", "override",
        "ignore", "event", "callback", "fires", "listens", "constructor", "extends",
        "implements", "abstract", "access", "alias", "author", "class", "classdesc",
        "constant", "const", "default", "defaultvalue", "description", "desc",
        "enum", "exports", "external", "file", "fileoverview", "function", "func",
        "generator", "global", "hideconstructor", "inheritdoc", "inner", "instance",
        "interface", "lends", "license", "link", "member", "memberof", "method",
        "mixin", "mixins", "module", "name", "namespace", "override", "package",
        "private", "protected", "public", "requires", "static", "summary",
        "this", "todo", "tutorial", "variation", "version", "yields", "yield",

        "media", "import", "keyframes", "include", "mixin", "extend", "use",
        "forward", "each", "for", "while", "if", "else", "function", "return",
        "charset", "namespace", "supports", "page", "font-face", "viewport",
        "layer", "apply", "tailwind", "screen", "responsive",

        "dependabot", "renovate", "renovate-bot", "github-actions", "actions-user",
        "snyk-bot", "greenkeeper", "semantic-release-bot", "allcontributors",
        "renovateapp", "imgbot", "deepsource-autofix",

        "types", "babel", "angular", "storybook", "nestjs", "vitest",

        "gmail", "outlook", "hotmail", "yahoo", "icloud",

        "match", "iterator", "dispose", "species", "latest", "next", "current",
        "deprecated", "experimental", "stable", "beta", "alpha", "rc",

        "here", "everyone", "all", "team", "me", "you", "us", "channel",
        "mention", "thanks", "thank",
    };

    private const int MaxMentionsPerNote = 20;

    public IEnumerable<string> ExtractMentions(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var matches = Regex.Matches(text, @"@([a-zA-Z0-9](?:[a-zA-Z0-9\-]{0,37}[a-zA-Z0-9])?)", RegexOptions.Compiled);
        return matches
            .Select(m => m.Groups[1].Value)
            .Where(u => u.Length >= 3)
            .Where(u => !System.Text.RegularExpressions.Regex.IsMatch(u, @"^\d+$"))
            .Where(u => !u.Contains('.'))
            .Where(u => !_mentionBlocklist.Contains(u))
            .Where(u => !u.StartsWith('-') && !u.EndsWith('-'))
            .Distinct(StringComparer.OrdinalIgnoreCase)
            // Each mention costs a GitHub lookup and the text is untrusted
            .Take(MaxMentionsPerNote);
    }
}

