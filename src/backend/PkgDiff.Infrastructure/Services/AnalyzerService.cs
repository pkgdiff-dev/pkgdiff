using System.Text.RegularExpressions;
using PkgDiff.Application.Interfaces;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Infrastructure.Services;

public class AnalyzerService : IAnalyzerService
{
    // Line-start prefix patterns → category mapping (most specific first)
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

    // Fallback keyword matching (for release notes without structured tags)
    private static readonly (string[] Keywords, ChangeCategory Category)[] KeywordRules =
    [
        (["breaking", "incompatible"], ChangeCategory.BreakingChange),
        (["security", "vulnerability", "cve"], ChangeCategory.Security),
        (["deprecat", "obsolete"], ChangeCategory.Deprecation),
        (["performance", "optimize", "faster"], ChangeCategory.Performance),
    ];

    /// <summary>
    /// Parses release notes into per-line entries with category detection.
    /// Detects tags at line start (e.g., "Fix - ...", "New feature - ...") and strips them from text.
    /// Tags mid-sentence are NOT removed.
    /// </summary>
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

            // Strip markdown list markers (-, *, •)
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

    /// <summary>
    /// Returns distinct categories found in release notes.
    /// Uses line-start tag detection first, falls back to keyword matching.
    /// </summary>
    public List<ChangeCategory> CategorizeReleaseNotes(string releaseNotes)
    {
        if (string.IsNullOrWhiteSpace(releaseNotes))
            return [ChangeCategory.Other];

        var entries = ParseReleaseNotes(releaseNotes);
        var categories = entries
            .Select(e => e.Category)
            .Where(c => c != ChangeCategory.Other)
            .ToHashSet();

        // If no specific categories found from line prefixes, try keyword fallback
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

    /// <summary>
    /// Detects a category tag at the start of a line and strips it.
    /// Returns (category, cleanedText). If no tag found, returns (Other, originalText).
    /// </summary>
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

                // Must be followed by a separator (-, :, –, —) or be the entire line
                if (rest.Length == 0)
                    return (category, string.Empty);

                if (rest[0] is '-' or ':' or '–' or '—')
                {
                    var text = rest[1..].TrimStart();
                    return (category, text);
                }

                // Also handle "Fix #123" or "Fixed something" patterns
                // (tag directly followed by description without separator)
                if (rest[0] is ' ' or '#')
                    return (category, rest.TrimStart());
            }
        }

        // Check for markdown heading patterns: ### Bug Fixes, ## New Features
        if (lower.StartsWith('#'))
        {
            var heading = line.TrimStart('#').Trim();
            var headingLower = heading.ToLowerInvariant();

            foreach (var (prefixes, category) in LinePrefixRules)
            {
                if (prefixes.Any(p => headingLower.StartsWith(p)))
                    return (category, string.Empty); // Headings are section titles, no text to keep
            }
        }

        return (ChangeCategory.Other, line);
    }

    /// <summary>
    /// Strips markdown list markers (-, *, •, numbered) from the start of a line.
    /// </summary>
    private static string StripListMarker(string line)
    {
        var trimmed = line.TrimStart();

        // "- text", "* text", "• text"
        if (trimmed.Length > 1 && trimmed[0] is '-' or '*' or '•' && trimmed[1] == ' ')
            return trimmed[2..].TrimStart();

        // "1. text", "2) text"
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

    // Common annotations, framework keywords, and bot names that are NOT GitHub usernames
    private static readonly HashSet<string> _mentionBlocklist = new(StringComparer.OrdinalIgnoreCase)
    {
        // JSDoc / JavaDoc annotations
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

        // CSS / SCSS / PostCSS directives  
        "media", "import", "keyframes", "include", "mixin", "extend", "use",
        "forward", "each", "for", "while", "if", "else", "function", "return",
        "charset", "namespace", "supports", "page", "font-face", "viewport",
        "layer", "apply", "tailwind", "screen", "responsive",

        // Common GitHub bots / CI accounts (add more as needed)
        "dependabot", "renovate", "renovate-bot", "github-actions", "actions-user",
        "snyk-bot", "greenkeeper", "semantic-release-bot", "allcontributors",
        "renovateapp", "imgbot", "deepsource-autofix",

        // npm scoped packages (e.g. @types/node, @babel/core) — org names not people
        "types", "babel", "angular", "storybook", "nestjs", "vitest",

        // Email domains commonly appearing in @user@domain.com patterns
        "gmail", "outlook", "hotmail", "yahoo", "icloud",

        // JavaScript / Node.js built-in symbols, methods, and reserved words
        "match", "iterator", "dispose", "species", "latest", "next", "current",
        "deprecated", "experimental", "stable", "beta", "alpha", "rc",

        // Misc words often used in changelog prose
        "here", "everyone", "all", "team", "me", "you", "us", "channel",
        "mention", "thanks", "thank",
    };

    /// <summary>
    /// Extracts GitHub @username mentions from release note text.
    /// Valid GitHub usernames: alphanumeric and hyphens, 1-39 chars.
    /// Filters out JSDoc/CSS annotations, known bots, and email-like patterns.
    /// </summary>
    private const int MaxMentionsPerNote = 20;

    public IEnumerable<string> ExtractMentions(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return [];

        var matches = Regex.Matches(text, @"@([a-zA-Z0-9](?:[a-zA-Z0-9\-]{0,37}[a-zA-Z0-9])?)", RegexOptions.Compiled);
        return matches
            .Select(m => m.Groups[1].Value)
            .Where(u => u.Length >= 3)                             // GH minimum meaningful length (filters '0','1','qq' etc)
            .Where(u => !System.Text.RegularExpressions.Regex.IsMatch(u, @"^\d+$")) // skip pure numbers (e.g. version refs like @16, @18)
            .Where(u => !u.Contains('.'))                          // skip email-like patterns
            .Where(u => !_mentionBlocklist.Contains(u))            // skip annotations & bots
            .Where(u => !u.StartsWith('-') && !u.EndsWith('-'))    // invalid username chars
            .Distinct(StringComparer.OrdinalIgnoreCase)
            // Each mention costs a GitHub lookup and the text is written by the package author
            .Take(MaxMentionsPerNote);
    }
}

