using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Microsoft.Extensions.Logging;

namespace PkgDiff.Infrastructure.Services;

// Reads a GitHub releases page: the releases on it, its page count, and the notes as Markdown
public static class GitHubReleasePage
{
    // Regex to extract semantic version from release titles like "Aspire 13.1.1", "v9.5.0 Release", "Package Name 1.2.3-preview.1"
    private static readonly Regex SemVerRegex = new(
        @"(?<version>\d+\.\d+\.\d+(?:[-\.][a-zA-Z0-9\.]+)?)",
        RegexOptions.Compiled);

    public static void ExtractReleases(IDocument document, Dictionary<string, string> releases, ILogger logger)
    {
        // Find all release sections
        var releaseSections = document.QuerySelectorAll("section[aria-labelledby]");
        logger.LogDebug("[GitHubScraper] Found {Count} release sections on current page", releaseSections.Length);

        foreach (var section in releaseSections)
        {
            // Extract version from the heading
            var versionElement = section.QuerySelector(".f1.text-bold a, h2 a");
            if (versionElement == null)
            {
                logger.LogDebug("[GitHubScraper] Skipping section - no version element found");
                continue;
            }

            var rawVersion = versionElement.TextContent.Trim();

            // Extract semantic version from release title (handles "Aspire 13.1.1", "v9.5.0 Release", etc.)
            var semVerMatch = SemVerRegex.Match(rawVersion);
            var version = semVerMatch.Success
                ? semVerMatch.Groups["version"].Value
                : rawVersion.TrimStart('v', 'V');

            // Skip if already added (avoid duplicates)
            if (releases.ContainsKey(version))
            {
                logger.LogDebug("[GitHubScraper] Skipping duplicate version {Version}", version);
                continue;
            }

            // Extract release notes from markdown body
            var notesElement = section.QuerySelector(".markdown-body");
            if (notesElement != null)
            {
                var notes = CleanReleaseNotes(HtmlToMarkdown(notesElement));
                if (!string.IsNullOrWhiteSpace(notes))
                {
                    releases[version] = notes;
                    logger.LogDebug("[GitHubScraper] Found release: {RawVersion} -> {CleanVersion} (notes length: {Length})",
                        rawVersion, version, notes.Length);
                }
                else
                {
                    logger.LogDebug("[GitHubScraper] Release {RawVersion} has empty notes after cleaning", rawVersion);
                }
            }
            else
            {
                logger.LogDebug("[GitHubScraper] Release {RawVersion} has no .markdown-body element", rawVersion);
            }
        }
    }

    public static int GetTotalPages(IDocument document)
    {
        // Try to find pagination element with data-total-pages attribute
        var paginationElement = document.QuerySelector("em.current[data-total-pages]");
        if (paginationElement != null)
        {
            var totalPagesAttr = paginationElement.GetAttribute("data-total-pages");
            if (int.TryParse(totalPagesAttr, out var totalPages))
            {
                return totalPages;
            }
        }

        // Fallback: check if there's a "Next" button (means at least 2 pages)
        var nextButton = document.QuerySelector("a.next_page");
        if (nextButton != null)
        {
            return 2; // At least 2 pages
        }

        return 1; // Only 1 page
    }

    public static string CleanReleaseNotes(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Normalize line endings
        text = text.Replace("\r\n", "\n").Replace("\r", "\n");

        // Runs of spaces inside a line collapse. Indentation stays: in Markdown it decides what belongs
        // to a list item and what is a code block, and code inside fences keeps every space
        text = CollapseInlineSpaces(text);

        // Collapse more than 2 consecutive blank lines into 2
        text = Regex.Replace(text, @"\n{3,}", "\n\n");

        return text.Trim();
    }

    private static readonly Regex InlineSpaces = new(@"(?<=\S)[ \t]{2,}(?=\S)", RegexOptions.Compiled);

    private static string CollapseInlineSpaces(string text)
    {
        var lines = text.Split('\n');
        var inFence = false;
        for (var i = 0; i < lines.Length; i++)
        {
            var content = lines[i].TrimStart();
            if (content.StartsWith("```", StringComparison.Ordinal) || content.StartsWith("~~~", StringComparison.Ordinal))
            {
                inFence = !inFence;
                continue;
            }
            if (inFence || content.Length == 0)
                continue;

            var indentation = lines[i][..(lines[i].Length - content.Length)];
            lines[i] = indentation + InlineSpaces.Replace(content, " ");
        }
        return string.Join('\n', lines);
    }

    private static string HtmlToMarkdown(AngleSharp.Dom.IElement element)
    {
        var sb = new System.Text.StringBuilder();
        ConvertNodeToMarkdown(element, sb);
        return sb.ToString().Trim();
    }

    private static void ConvertNodeToMarkdown(AngleSharp.Dom.INode node, System.Text.StringBuilder sb)
    {
        if (node is AngleSharp.Dom.IText textNode)
        {
            sb.Append(textNode.TextContent);
            return;
        }

        if (node is not AngleSharp.Dom.IElement el) return;

        switch (el.TagName.ToLowerInvariant())
        {
            case "h1": sb.Append("# "); AppendChildren(el, sb); sb.Append("\n\n"); break;
            case "h2": sb.Append("## "); AppendChildren(el, sb); sb.Append("\n\n"); break;
            case "h3": sb.Append("### "); AppendChildren(el, sb); sb.Append("\n\n"); break;
            case "h4": sb.Append("#### "); AppendChildren(el, sb); sb.Append("\n\n"); break;
            case "ul": case "ol": AppendChildren(el, sb); sb.Append("\n"); break;
            case "li": sb.Append("- "); AppendChildren(el, sb); sb.Append("\n"); break;
            case "p": AppendChildren(el, sb); sb.Append("\n\n"); break;
            case "br": sb.Append("\n"); break;
            case "code": sb.Append('`'); sb.Append(el.TextContent); sb.Append('`'); break;
            case "pre": sb.Append("```\n"); sb.Append(el.TextContent.Trim()); sb.Append("\n```\n\n"); break;
            case "strong": case "b": sb.Append("**"); AppendChildren(el, sb); sb.Append("**"); break;
            case "em": case "i": sb.Append("*"); AppendChildren(el, sb); sb.Append("*"); break;
            case "a":
                var href = el.GetAttribute("href") ?? "#";
                sb.Append('['); AppendChildren(el, sb); sb.Append("]("); sb.Append(href); sb.Append(')');
                break;
            case "hr": sb.Append("\n---\n\n"); break;
            case "kbd": sb.Append('`'); sb.Append(el.TextContent); sb.Append('`'); break;
            default: AppendChildren(el, sb); break;
        }
    }

    private static void AppendChildren(AngleSharp.Dom.IElement el, System.Text.StringBuilder sb)
    {
        foreach (var child in el.ChildNodes)
            ConvertNodeToMarkdown(child, sb);
    }
}
