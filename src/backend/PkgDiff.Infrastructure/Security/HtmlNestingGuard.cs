namespace PkgDiff.Infrastructure.Security;

// AngleSharp recurses over the DOM, so a deeply nested page overflows the stack and kills the process.
// Depth is measured first without recursion; optional-end-tag and void elements do not count.
public static class HtmlNestingGuard
{
    public const int MaxDepth = 512;

    private static readonly HashSet<string> Uncounted = new(StringComparer.OrdinalIgnoreCase)
    {
        // void elements
        "area", "base", "br", "col", "embed", "hr", "img", "input", "link", "meta", "param", "source", "track", "wbr",
        // elements the parser closes by itself
        "p", "li", "dt", "dd", "option", "optgroup", "tr", "td", "th", "thead", "tbody", "tfoot", "colgroup",
        "caption", "rb", "rt", "rtc", "rp", "html", "head", "body",
    };

    private static readonly string[] RawText = { "script", "style", "textarea", "title", "xmp", "plaintext" };

    public static bool IsTooDeep(string html, int maxDepth = MaxDepth)
    {
        var depth = 0;
        var i = 0;
        while ((i = html.IndexOf('<', i)) >= 0)
        {
            if (i + 1 >= html.Length)
                break;

            var next = html[i + 1];
            if (next == '!' || next == '?')
            {
                var end = string.CompareOrdinal(html, i, "<!--", 0, 4) == 0
                    ? html.IndexOf("-->", i + 4, StringComparison.Ordinal)
                    : html.IndexOf('>', i + 2);
                if (end < 0)
                    break;
                i = end + 1;
                continue;
            }

            var closing = next == '/';
            var nameStart = closing ? i + 2 : i + 1;
            var nameEnd = nameStart;
            while (nameEnd < html.Length && (char.IsLetterOrDigit(html[nameEnd]) || html[nameEnd] == '-'))
                nameEnd++;
            if (nameEnd == nameStart)
            {
                i++;
                continue;
            }

            var name = html[nameStart..nameEnd];
            var tagEnd = html.IndexOf('>', nameEnd);
            if (tagEnd < 0)
                break;

            if (closing)
            {
                if (!Uncounted.Contains(name) && depth > 0)
                    depth--;
            }
            else if (RawText.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                // Its content is text, whatever it looks like
                var close = html.IndexOf("</" + name, tagEnd, StringComparison.OrdinalIgnoreCase);
                if (close < 0)
                    break;
                tagEnd = close;
            }
            else if (!Uncounted.Contains(name) && html[tagEnd - 1] != '/' && ++depth > maxDepth)
            {
                return true;
            }

            i = tagEnd + 1;
        }

        return false;
    }
}
