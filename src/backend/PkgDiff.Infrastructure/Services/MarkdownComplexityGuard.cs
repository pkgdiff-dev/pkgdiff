namespace PkgDiff.Infrastructure.Services;

// Rejects input nested deep enough to overflow the recursive parsers' stack, which ends the
// process. Linear scan that over-estimates depth so hostile input is rejected early.
internal static class MarkdownComplexityGuard
{
    // Also bounds the sanitizer's superlinear cost on hostile notes
    internal const int MaxLength = 128 * 1024;

    // The HTML parser builds at most about twice the counted depth, far below the crash depth
    internal const int MaxDepth = 64;

    internal enum Limit
    {
        None,
        Length,
        BlockNesting,
        BracketNesting,
        ParenthesisNesting,
        EmphasisNesting,
        BacktickRun,
        HtmlNesting
    }

    // Only allowlisted tags reach the sanitizer as elements; every other tag is rendered as text
    private static readonly HashSet<string>.AlternateLookup<ReadOnlySpan<char>> ElementTags =
        ReleaseNotesRenderer.AllowedTagSet.GetAlternateLookup<ReadOnlySpan<char>>();

    private static readonly HashSet<string> VoidTags = new(StringComparer.Ordinal) { "br", "img", "hr" };

    // The adoption agency may keep or re-create these, so only an exact match closes them
    private static readonly HashSet<string> FormattingTags = new(StringComparer.Ordinal)
    {
        "a", "b", "strong", "i", "em", "code", "s"
    };

    private static readonly HashSet<string> ImpliedEndTags = new(StringComparer.Ordinal)
    {
        "p", "li", "td", "th", "tr", "thead", "tbody", "tfoot"
    };

    private static readonly HashSet<string> ParagraphOnly = new(StringComparer.Ordinal) { "p" };

    private static readonly HashSet<string> ParagraphOrListItem = new(StringComparer.Ordinal) { "p", "li" };

    private static readonly HashSet<string> Cells = new(StringComparer.Ordinal) { "td", "th" };

    private static readonly HashSet<string> TableSections = new(StringComparer.Ordinal) { "thead", "tbody", "tfoot" };

    private static readonly HashSet<string> Headings = new(StringComparer.Ordinal) { "h1", "h2", "h3", "h4", "h5", "h6" };

    private static readonly HashSet<string> ClosesParagraph = new(StringComparer.Ordinal)
    {
        "p", "div", "blockquote", "details", "summary", "h1", "h2", "h3", "h4", "h5", "h6", "ol", "ul", "pre"
    };

    internal static Limit Check(string markdown, out int measured)
    {
        measured = markdown.Length;
        if (markdown.Length > MaxLength)
            return Limit.Length;

        var state = new ScanState();
        var lineStart = 0;
        while (lineStart < markdown.Length)
        {
            var lineEnd = markdown.IndexOf('\n', lineStart);
            if (lineEnd < 0)
                lineEnd = markdown.Length;

            var limit = CheckLine(markdown.AsSpan(lineStart, lineEnd - lineStart), state, out measured);
            if (limit != Limit.None)
                return limit;

            lineStart = lineEnd + 1;
        }

        measured = 0;
        return Limit.None;
    }

    private static Limit CheckLine(ReadOnlySpan<char> line, ScanState state, out int measured)
    {
        // Raw HTML is counted everywhere, code included: over-counting code only costs formatting
        var limit = CheckHtml(line, state.OpenElements, out measured);
        if (limit != Limit.None)
            return limit;

        var position = 0;
        var containers = 0;
        var indentation = 0;
        while (position < line.Length)
        {
            var c = line[position];
            if (c is ' ' or '\r')
            {
                indentation++;
                position++;
            }
            else if (c == '\t')
            {
                indentation += 4;
                position++;
            }
            else if (c == '>')
            {
                containers++;
                position++;
            }
            else if (ListMarkerLength(line[position..]) is var markerLength and > 0)
            {
                containers++;
                position += markerLength;
            }
            else
            {
                break;
            }
        }

        var rest = line[position..];

        if (state.FenceChar != '\0')
        {
            // Code lines only matter for the closing fence; Markdig's own depth limit backs this up
            var run = RunLength(rest, 0);
            if (run >= state.FenceLength && rest.Length > 0 && rest[0] == state.FenceChar && rest[run..].IsWhiteSpace())
                state.CloseFence();

            return Limit.None;
        }

        // Each '>' or list marker opens one level, and nested list items sit at least two columns deeper
        measured = containers + Math.Max(0, indentation - containers) / 2;
        if (measured > MaxDepth)
            return Limit.BlockNesting;

        if (rest.Length > 0 && rest[0] is '`' or '~')
        {
            var run = RunLength(rest, 0);
            if (rest[0] == '`' && run > MaxDepth)
            {
                measured = run;
                return Limit.BacktickRun;
            }

            if (run >= 3 && (rest[0] == '~' || !rest[run..].Contains('`')))
            {
                state.OpenFence(rest[0], run);
                return Limit.None;
            }
        }

        // Inline markup never continues across a blank line or into a new block
        if (rest.IsWhiteSpace())
        {
            state.ResetInline();
            return Limit.None;
        }

        if (containers > 0 || rest[0] == '#')
            state.ResetInline();

        return CheckInline(rest, state, out measured);
    }

    private static Limit CheckInline(ReadOnlySpan<char> text, ScanState state, out int measured)
    {
        measured = 0;
        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];
            switch (c)
            {
                case '\\':
                    i++;
                    break;

                case '`':
                {
                    var run = RunLength(text, i);
                    if (run > MaxDepth)
                    {
                        measured = run;
                        return Limit.BacktickRun;
                    }

                    i += run - 1;
                    break;
                }

                case '[':
                    if (++state.Brackets > MaxDepth)
                    {
                        measured = state.Brackets;
                        return Limit.BracketNesting;
                    }

                    break;

                case ']':
                    if (state.Brackets > 0)
                        state.Brackets--;
                    break;

                case '(':
                    if (++state.Parentheses > MaxDepth)
                    {
                        measured = state.Parentheses;
                        return Limit.ParenthesisNesting;
                    }

                    break;

                case ')':
                    if (state.Parentheses > 0)
                        state.Parentheses--;
                    break;

                case '*' or '_' or '~':
                {
                    var run = RunLength(text, i);

                    // A single '~' is plain text; only '~~' is strikethrough
                    if (c != '~' || run >= 2)
                    {
                        var before = i > 0 ? text[i - 1] : ' ';
                        var after = i + run < text.Length ? text[i + run] : ' ';
                        var (canOpen, canClose) = Flanking(c, before, after);

                        if (canOpen && (!canClose || state.Emphasis == 0))
                        {
                            state.Emphasis += run;
                            if (state.Emphasis > MaxDepth)
                            {
                                measured = state.Emphasis;
                                return Limit.EmphasisNesting;
                            }
                        }
                        else if (canClose)
                        {
                            state.Emphasis = Math.Max(0, state.Emphasis - run);
                        }
                    }

                    i += run - 1;
                    break;
                }
            }
        }

        return Limit.None;
    }

    // CommonMark delimiter-run rules: which side of the run touches text decides whether it opens or closes
    private static (bool CanOpen, bool CanClose) Flanking(char delimiter, char before, char after)
    {
        var beforeSpace = char.IsWhiteSpace(before);
        var afterSpace = char.IsWhiteSpace(after);
        var beforePunctuation = char.IsPunctuation(before) || char.IsSymbol(before);
        var afterPunctuation = char.IsPunctuation(after) || char.IsSymbol(after);

        var leftFlanking = !afterSpace && (!afterPunctuation || beforeSpace || beforePunctuation);
        var rightFlanking = !beforeSpace && (!beforePunctuation || afterSpace || afterPunctuation);

        if (delimiter == '_')
            return (leftFlanking && (!rightFlanking || beforePunctuation), rightFlanking && (!leftFlanking || afterPunctuation));

        return (leftFlanking, rightFlanking);
    }

    // Mirrors the parser's open elements but closes only when it certainly would (never undercounts)
    private static Limit CheckHtml(ReadOnlySpan<char> line, List<string> openElements, out int measured)
    {
        measured = 0;
        var i = line.IndexOf('<');
        while (i >= 0 && i < line.Length)
        {
            var closing = i + 1 < line.Length && line[i + 1] == '/';
            var nameStart = i + (closing ? 2 : 1);
            var nameEnd = nameStart;
            while (nameEnd < line.Length && (char.IsAsciiLetterOrDigit(line[nameEnd]) || line[nameEnd] == '-'))
                nameEnd++;

            var isTag = nameEnd > nameStart
                && char.IsAsciiLetter(line[nameStart])
                && (nameEnd == line.Length || char.IsWhiteSpace(line[nameEnd]) || line[nameEnd] == '>' || (!closing && line[nameEnd] == '/'));

            if (isTag && ElementTags.TryGetValue(line[nameStart..nameEnd], out var name) && !VoidTags.Contains(name))
            {
                if (closing)
                {
                    CloseElement(openElements, name);
                }
                else
                {
                    OpenElement(openElements, name);
                    if (openElements.Count > MaxDepth)
                    {
                        measured = openElements.Count;
                        return Limit.HtmlNesting;
                    }
                }
            }

            var next = line[(i + 1)..].IndexOf('<');
            i = next < 0 ? -1 : i + 1 + next;
        }

        return Limit.None;
    }

    private static void OpenElement(List<string> openElements, string name)
    {
        if (name == "li")
        {
            TruncateAt(openElements, FindOpen(openElements, "li", ParagraphOnly));
        }
        else if (Cells.Contains(name))
        {
            TruncateAt(openElements, FindOpenAny(openElements, Cells, ParagraphOrListItem));
        }
        else if (name == "tr" || TableSections.Contains(name))
        {
            TruncateAt(openElements, FindOpenAny(openElements, Cells, ParagraphOrListItem));
            if (Top(openElements) == "tr")
                openElements.RemoveAt(openElements.Count - 1);
            if (TableSections.Contains(name) && TableSections.Contains(Top(openElements) ?? string.Empty))
                openElements.RemoveAt(openElements.Count - 1);
        }
        else if (Headings.Contains(name) && Headings.Contains(Top(openElements) ?? string.Empty))
        {
            openElements.RemoveAt(openElements.Count - 1);
        }

        if (ClosesParagraph.Contains(name) && Top(openElements) == "p")
            openElements.RemoveAt(openElements.Count - 1);

        openElements.Add(name);
    }

    private static void CloseElement(List<string> openElements, string name)
    {
        if (FormattingTags.Contains(name))
        {
            if (Top(openElements) == name)
                openElements.RemoveAt(openElements.Count - 1);
            return;
        }

        TruncateAt(openElements, FindOpen(openElements, name, ImpliedEndTags));
    }

    // Topmost element named `name`, provided only elements from `over` lie above it
    private static int FindOpen(List<string> openElements, string name, HashSet<string> over)
    {
        for (var i = openElements.Count - 1; i >= 0; i--)
        {
            if (openElements[i] == name)
                return i;
            if (!over.Contains(openElements[i]))
                return -1;
        }

        return -1;
    }

    private static int FindOpenAny(List<string> openElements, HashSet<string> names, HashSet<string> over)
    {
        for (var i = openElements.Count - 1; i >= 0; i--)
        {
            if (names.Contains(openElements[i]))
                return i;
            if (!over.Contains(openElements[i]))
                return -1;
        }

        return -1;
    }

    private static void TruncateAt(List<string> openElements, int index)
    {
        if (index >= 0)
            openElements.RemoveRange(index, openElements.Count - index);
    }

    private static string? Top(List<string> openElements) =>
        openElements.Count > 0 ? openElements[^1] : null;

    private static int ListMarkerLength(ReadOnlySpan<char> text)
    {
        if (text[0] is '-' or '+' or '*')
            return text.Length == 1 || text[1] is ' ' or '\t' ? 1 : 0;

        var digits = 0;
        while (digits < text.Length && digits < 9 && char.IsAsciiDigit(text[digits]))
            digits++;

        if (digits == 0 || digits >= text.Length || text[digits] is not ('.' or ')'))
            return 0;

        return digits + 1 == text.Length || text[digits + 1] is ' ' or '\t' ? digits + 1 : 0;
    }

    private static int RunLength(ReadOnlySpan<char> text, int start)
    {
        if (start >= text.Length)
            return 0;

        var end = start;
        while (end < text.Length && text[end] == text[start])
            end++;

        return end - start;
    }

    private sealed class ScanState
    {
        public List<string> OpenElements { get; } = new();

        public char FenceChar { get; private set; }

        public int FenceLength { get; private set; }

        public int Brackets { get; set; }

        public int Parentheses { get; set; }

        public int Emphasis { get; set; }

        public void OpenFence(char fenceChar, int length)
        {
            FenceChar = fenceChar;
            FenceLength = length;
            ResetInline();
        }

        public void CloseFence()
        {
            FenceChar = '\0';
            FenceLength = 0;
        }

        public void ResetInline()
        {
            Brackets = 0;
            Parentheses = 0;
            Emphasis = 0;
        }
    }
}
