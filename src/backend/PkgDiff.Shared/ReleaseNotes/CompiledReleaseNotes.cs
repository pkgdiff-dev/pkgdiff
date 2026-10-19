using System.Text.RegularExpressions;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Shared.ReleaseNotes;

// Notes pkgdiff compiles itself (from a GitHub milestone, when the maintainers publish no changelog) start
// with a notice line naming their source. The stored notes keep it, so copies and exports say it too; the
// page shows it as a warning apart from the notes.
public static partial class CompiledReleaseNotes
{
    private const string Marker = "Compiled by pkgdiff from the closed issues and pull requests of the ";

    public static string WithNotice(string notes, string label, string url) =>
        $"> {Marker}[{label}]({url}); the maintainers publish no changelog for this version.\n\n{notes}";

    // The notes without the notice and the source it names; no source for notes the maintainers wrote
    public static (string Body, CompiledNotesSource? Source) Split(string? notes)
    {
        notes ??= string.Empty;
        if (!notes.StartsWith("> " + Marker, StringComparison.Ordinal))
            return (notes, null);

        var lineEnd = notes.IndexOf('\n');
        var notice = lineEnd < 0 ? notes : notes[..lineEnd];
        var body = lineEnd < 0 ? string.Empty : notes[(lineEnd + 1)..].TrimStart('\r', '\n');

        var link = NoticeLink().Match(notice);
        return (body, new CompiledNotesSource
        {
            Label = link.Success ? link.Groups["label"].Value : "GitHub milestone",
            Url = link.Success ? link.Groups["url"].Value : null
        });
    }

    // The notice as an entry of notes parsed before it was told apart
    public static bool IsNotice(string entryText) =>
        entryText.TrimStart('>', ' ').StartsWith(Marker, StringComparison.Ordinal);

    [GeneratedRegex(@"\[(?<label>[^\]]+)\]\((?<url>https://github\.com/[^)\s]+)\)")]
    private static partial Regex NoticeLink();
}
