using System.Text.RegularExpressions;

namespace PkgDiff.Application.Services;

public static class AutoPilotQualityGate
{
    public const int MinimumLength = 300;
    public const string OverviewHeading = "## Overview";
    public const string WhatsChangedHeading = "## What's changed";
    public const string UpgradeNotesHeading = "## Upgrade notes";

    private const double MaxDuplicateLineRatio = 0.30;

    private static readonly Regex SectionHeading = new(@"^#{2,6} ", RegexOptions.Compiled);
    private static readonly string[] RequiredHeadings = { OverviewHeading, WhatsChangedHeading, UpgradeNotesHeading };

    public static bool Passes(string cleanedMarkdown, string packageId, out string reason)
    {
        var text = (cleanedMarkdown ?? string.Empty).Trim();
        if (text.Length < MinimumLength)
        {
            reason = "too short";
            return false;
        }

        var lines = text.Split('\n').Select(l => l.Trim()).ToList();
        foreach (var heading in RequiredHeadings)
        {
            if (!lines.Contains(heading, StringComparer.Ordinal))
            {
                reason = $"missing required section: {heading}";
                return false;
            }
        }

        if (!MentionsPackage(text, packageId))
        {
            reason = "does not mention the package";
            return false;
        }

        var repeatedHeading = lines
            .Where(l => SectionHeading.IsMatch(l))
            .GroupBy(l => l, StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (repeatedHeading != null)
        {
            reason = $"repetition: heading repeated: {repeatedHeading.Key}";
            return false;
        }

        var nonEmpty = lines.Where(l => l.Length > 0).ToList();
        var distinct = nonEmpty.Distinct(StringComparer.OrdinalIgnoreCase).Count();
        if ((nonEmpty.Count - distinct) / (double)nonEmpty.Count > MaxDuplicateLineRatio)
        {
            reason = "repetition: too many duplicate lines";
            return false;
        }

        reason = string.Empty;
        return true;
    }

    // npm scoped ids ("@scope/name") are usually written as just "name" in prose
    private static bool MentionsPackage(string text, string packageId)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            return false;

        if (text.Contains(packageId, StringComparison.OrdinalIgnoreCase))
            return true;

        var slash = packageId.LastIndexOf('/');
        if (slash < 0 || slash == packageId.Length - 1)
            return false;

        return text.Contains(packageId[(slash + 1)..], StringComparison.OrdinalIgnoreCase);
    }
}
