using System.Text;

namespace PkgDiff.Application.Services;

// Markdown is stored as obtained and only made safe when rendered (IReleaseNotesRenderer). Storage only
// drops control characters: PostgreSQL text rejects NUL and the others carry no markdown meaning.
public static class StoredMarkdown
{
    public static string Normalize(string? markdown)
    {
        if (string.IsNullOrEmpty(markdown))
            return string.Empty;

        var firstControl = IndexOfDisallowedControl(markdown);
        if (firstControl < 0)
            return markdown;

        var builder = new StringBuilder(markdown.Length);
        builder.Append(markdown, 0, firstControl);
        for (var i = firstControl; i < markdown.Length; i++)
        {
            if (!IsDisallowedControl(markdown[i]))
                builder.Append(markdown[i]);
        }

        return builder.ToString();
    }

    private static int IndexOfDisallowedControl(string text)
    {
        for (var i = 0; i < text.Length; i++)
        {
            if (IsDisallowedControl(text[i]))
                return i;
        }

        return -1;
    }

    private static bool IsDisallowedControl(char c) =>
        char.IsControl(c) && c is not ('\t' or '\n' or '\r');
}
