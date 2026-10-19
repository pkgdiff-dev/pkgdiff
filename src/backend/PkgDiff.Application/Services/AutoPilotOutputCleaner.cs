using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;

namespace PkgDiff.Application.Services;

public static class AutoPilotOutputCleaner
{
    private static readonly Regex HeadingPrefix = new(@"^(#{1,6})[ \t]+", RegexOptions.Compiled);
    private static readonly Regex H1Line = new(@"^#(?:[ \t]|$)", RegexOptions.Compiled);
    private static readonly Regex HashesOnly = new(@"^#{1,6}$", RegexOptions.Compiled);
    private static readonly Regex HeadingLine = new(@"^(#{1,6})[ \t]*(.+?)[ \t#]*$", RegexOptions.Compiled);
    private static readonly Regex EmphasisLine = new(@"^(\*\*|__)(.+?)\1[ \t]*:?$", RegexOptions.Compiled);
    private static readonly Regex LeadingNumber = new(@"^(?:\d+[.)]|[IVX]+\.)[ \t]*", RegexOptions.Compiled);
    private static readonly Regex SectionHeading = new(@"^#{2,6} ", RegexOptions.Compiled);
    private static readonly Regex InnerWhitespace = new(@"[ \t]+", RegexOptions.Compiled);
    private static readonly Regex AnyWhitespace = new(@"\s+", RegexOptions.Compiled);

    private static readonly string[] CanonicalHeadings =
    {
        AutoPilotQualityGate.OverviewHeading,
        AutoPilotQualityGate.WhatsChangedHeading,
        AutoPilotQualityGate.UpgradeNotesHeading
    };

    // Small models rarely reproduce the exact heading text; these are the variants seen in practice.
    // Synonyms only apply to top-level headings (H1/H2), bold lines and bare lines, so an H3 such as
    // "### Changes" inside a section is not promoted to a second required section.
    private static readonly Dictionary<string, string> HeadingSynonyms = new(StringComparer.Ordinal)
    {
        ["summary"] = AutoPilotQualityGate.OverviewHeading,
        ["introduction"] = AutoPilotQualityGate.OverviewHeading,
        ["intro"] = AutoPilotQualityGate.OverviewHeading,
        ["about"] = AutoPilotQualityGate.OverviewHeading,
        ["release overview"] = AutoPilotQualityGate.OverviewHeading,
        ["what changed"] = AutoPilotQualityGate.WhatsChangedHeading,
        ["what has changed"] = AutoPilotQualityGate.WhatsChangedHeading,
        ["whats changed"] = AutoPilotQualityGate.WhatsChangedHeading,
        ["changes"] = AutoPilotQualityGate.WhatsChangedHeading,
        ["key changes"] = AutoPilotQualityGate.WhatsChangedHeading,
        ["notable changes"] = AutoPilotQualityGate.WhatsChangedHeading,
        ["what's new"] = AutoPilotQualityGate.WhatsChangedHeading,
        ["whats new"] = AutoPilotQualityGate.WhatsChangedHeading,
        ["what is new"] = AutoPilotQualityGate.WhatsChangedHeading,
        ["new features"] = AutoPilotQualityGate.WhatsChangedHeading,
        ["highlights"] = AutoPilotQualityGate.WhatsChangedHeading,
        ["release highlights"] = AutoPilotQualityGate.WhatsChangedHeading,
        ["changelog"] = AutoPilotQualityGate.WhatsChangedHeading,
        ["upgrade"] = AutoPilotQualityGate.UpgradeNotesHeading,
        ["upgrading"] = AutoPilotQualityGate.UpgradeNotesHeading,
        ["upgrade guide"] = AutoPilotQualityGate.UpgradeNotesHeading,
        ["upgrade instructions"] = AutoPilotQualityGate.UpgradeNotesHeading,
        ["how to upgrade"] = AutoPilotQualityGate.UpgradeNotesHeading,
        ["migration"] = AutoPilotQualityGate.UpgradeNotesHeading,
        ["migration notes"] = AutoPilotQualityGate.UpgradeNotesHeading,
        ["migration guide"] = AutoPilotQualityGate.UpgradeNotesHeading
    };

    private readonly record struct CleanLine(string Text, bool IsCode);

    public static string Clean(string llmMarkdown, string sourceReleaseNotes)
    {
        if (string.IsNullOrWhiteSpace(llmMarkdown))
            return string.Empty;

        var lines = NormalizeNewlines(llmMarkdown).Split('\n');
        var filtered = KeepOnlyQuotedCodeBlocks(lines, sourceReleaseNotes ?? string.Empty);
        var normalized = NormalizeLines(filtered);
        var withoutDuplicateSections = RemoveDuplicateSections(normalized);
        return JoinWithoutDuplicateParagraphs(withoutDuplicateSections);
    }

    private static string NormalizeNewlines(string text) =>
        text.Replace("\r\n", "\n").Replace('\r', '\n');

    private static string NormalizeWhitespace(string text) =>
        AnyWhitespace.Replace(text, " ").Trim();

    private static string? GetFenceMarker(string line)
    {
        var trimmed = line.TrimStart();
        if (trimmed.StartsWith("```", StringComparison.Ordinal))
            return "```";
        if (trimmed.StartsWith("~~~", StringComparison.Ordinal))
            return "~~~";
        return null;
    }

    // Small models invent code samples; only code that is quoted from the release notes is trustworthy
    private static List<CleanLine> KeepOnlyQuotedCodeBlocks(string[] lines, string sourceReleaseNotes)
    {
        var normalizedNotes = NormalizeWhitespace(sourceReleaseNotes);
        var result = new List<CleanLine>(lines.Length);
        var index = 0;

        while (index < lines.Length)
        {
            var fence = GetFenceMarker(lines[index]);
            if (fence == null)
            {
                result.Add(new CleanLine(lines[index], false));
                index++;
                continue;
            }

            var close = index + 1;
            while (close < lines.Length && !lines[close].TrimStart().StartsWith(fence, StringComparison.Ordinal))
                close++;

            var isClosed = close < lines.Length;
            var code = NormalizeWhitespace(string.Join('\n', lines[(index + 1)..close]));
            var lastLine = isClosed ? close : lines.Length - 1;

            if (code.Length > 0 && normalizedNotes.Contains(code, StringComparison.Ordinal))
            {
                for (var i = index; i <= lastLine; i++)
                    result.Add(new CleanLine(lines[i], true));
            }

            index = lastLine + 1;
        }

        return result;
    }

    private static List<CleanLine> NormalizeLines(List<CleanLine> lines)
    {
        var result = new List<CleanLine>(lines.Count);

        foreach (var line in lines)
        {
            var text = StripEmoji(line.Text);

            if (line.IsCode)
            {
                result.Add(new CleanLine(text, true));
                continue;
            }

            text = NormalizeSpacing(text);
            var trimmed = text.Trim();

            // Canonicalize before dropping H1 lines so "# What's changed" still counts as the section
            var canonical = CanonicalizeHeading(trimmed);
            if (canonical != null)
            {
                result.Add(new CleanLine(canonical, false));
                continue;
            }

            if (H1Line.IsMatch(trimmed) || HashesOnly.IsMatch(trimmed))
                continue;

            if (text.Length == 0 && (result.Count == 0 || IsBlank(result[^1])))
                continue;

            result.Add(new CleanLine(text, false));
        }

        return result;
    }

    private static List<CleanLine> RemoveDuplicateSections(List<CleanLine> lines)
    {
        var sections = new List<List<CleanLine>> { new() };
        foreach (var line in lines)
        {
            if (IsHeading(line) && sections[^1].Count > 0)
                sections.Add(new List<CleanLine>());
            sections[^1].Add(line);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<CleanLine>(lines.Count);
        foreach (var section in sections)
        {
            var key = NormalizeWhitespace(string.Join('\n', section.Select(l => l.Text)));
            if (section.Count > 0 && IsHeading(section[0]) && !seen.Add(key))
                continue;
            result.AddRange(section);
        }

        return result;
    }

    // Headings are never removed here so a repeated heading with different content still reaches the gate
    private static string JoinWithoutDuplicateParagraphs(List<CleanLine> lines)
    {
        var paragraphs = new List<List<CleanLine>> { new() };
        foreach (var line in lines)
        {
            if (IsBlank(line))
            {
                if (paragraphs[^1].Count > 0)
                    paragraphs.Add(new List<CleanLine>());
                continue;
            }
            paragraphs[^1].Add(line);
        }

        var seen = new HashSet<string>(StringComparer.Ordinal);
        var kept = new List<string>();
        foreach (var paragraph in paragraphs.Where(p => p.Count > 0))
        {
            var text = string.Join('\n', paragraph.Select(l => l.Text));
            if (!paragraph.Any(IsHeading) && !seen.Add(NormalizeWhitespace(text)))
                continue;
            kept.Add(text);
        }

        return string.Join("\n\n", kept).Trim();
    }

    private static bool IsBlank(CleanLine line) => !line.IsCode && line.Text.Length == 0;

    private static bool IsHeading(CleanLine line) => !line.IsCode && SectionHeading.IsMatch(line.Text.TrimStart());

    private static string StripEmoji(string line)
    {
        var builder = new StringBuilder(line.Length);
        foreach (var rune in line.EnumerateRunes())
        {
            if (!IsEmojiRune(rune))
                builder.Append(rune.ToString());
        }
        return builder.ToString();
    }

    private static bool IsEmojiRune(Rune rune)
    {
        var value = rune.Value;
        return Rune.GetUnicodeCategory(rune) == UnicodeCategory.OtherSymbol
            || (value >= 0x1F000 && value <= 0x1FBFF)
            || (value >= 0xFE00 && value <= 0xFE0F)
            || (value >= 0xE0100 && value <= 0xE01EF)
            || value == 0x200D
            || value == 0x20E3
            || (value >= 0xE0020 && value <= 0xE007F);
    }

    // Leading indentation is kept so nested list items keep their level
    private static string NormalizeSpacing(string line)
    {
        var start = 0;
        while (start < line.Length && (line[start] == ' ' || line[start] == '\t'))
            start++;

        if (start == line.Length)
            return string.Empty;

        var body = InnerWhitespace.Replace(line[start..], " ").TrimEnd();
        body = HeadingPrefix.Replace(body, "$1 ");
        return line[..start] + body;
    }

    // Returns the canonical "## ..." heading when the line is a required section in any common form
    // (H1-H3, **bold**, bare "Summary:" line, numbered, curly apostrophe), otherwise null
    private static string? CanonicalizeHeading(string trimmed)
    {
        if (trimmed.Length == 0)
            return null;

        string text;
        var allowSynonyms = true;
        var heading = HeadingLine.Match(trimmed);
        if (heading.Success)
        {
            var level = heading.Groups[1].Length;
            if (level > 3)
                return null;
            allowSynonyms = level <= 2;
            text = heading.Groups[2].Value;
        }
        else
        {
            var emphasis = EmphasisLine.Match(trimmed);
            text = emphasis.Success ? emphasis.Groups[2].Value : trimmed;
        }

        var key = HeadingKey(text);
        foreach (var canonical in CanonicalHeadings)
        {
            if (key == HeadingKey(canonical[3..]))
                return canonical;
        }

        return allowSynonyms && HeadingSynonyms.TryGetValue(key, out var synonym) ? synonym : null;
    }

    private static string HeadingKey(string text)
    {
        var key = text.Replace('’', '\'').Replace('‘', '\'').Replace("**", "").Replace("__", "").Replace("`", "");
        key = LeadingNumber.Replace(NormalizeWhitespace(key), "");
        return key.TrimEnd(':', '.', ' ').ToLowerInvariant();
    }
}
