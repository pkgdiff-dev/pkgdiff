using System.Net;
using System.Text;
using Microsoft.Extensions.Logging;
using PkgDiff.Infrastructure.Services;

namespace PkgDiff.Tests.Content;

// Inputs stay far below stack-overflow depth: a crash-depth input would take down the test host
public class ReleaseNotesNestingGuardTests
{
    private const string Marker = "SECRET-NOTE-TEXT";

    public static TheoryData<string, string> TooDeeplyNested() => new()
    {
        { "HtmlNesting", Repeat("<div>", 200) + Marker },
        { "HtmlNesting", "x " + Repeat("<b>", 500) + Marker },
        { "HtmlNesting", Repeat("<span>", 100) + Marker + Repeat("</span>", 100) },
        { "HtmlNesting", Repeat("<table><tr><td>", 100) + Marker },
        { "HtmlNesting", Repeat("<ul><li>", 100) + Marker },
        { "HtmlNesting", Repeat("<details>", 100) + Marker },
        { "HtmlNesting", "x " + Repeat("<b><div></b>", 100) + Marker },
        { "HtmlNesting", "x " + Repeat("<b><div></b></i></span>", 100) + Marker },
        { "HtmlNesting", "<div>" + Numbered("<b title=\"{0}\">", 100) + "</div>\n\n" + Marker },
        { "HtmlNesting", "<table>" + Numbered("<b title=\"{0}\">", 100) + "</table>" + Marker },
        { "BlockNesting", Repeat("> ", 200) + Marker },
        { "BlockNesting", string.Concat(Enumerable.Range(1, 100).Select(i => Repeat(">", i) + " a\n")) + Marker },
        { "BlockNesting", Repeat("- ", 200) + Marker },
        { "BlockNesting", Repeat("> - ", 100) + Marker },
        { "BlockNesting", string.Concat(Enumerable.Range(0, 100).Select(i => new string(' ', i * 2) + "- a\n")) + Marker },
        { "BracketNesting", Repeat("[", 300) + Marker + Repeat("]", 300) },
        { "BracketNesting", Repeat("[", 100) + Marker + Repeat("](b)", 100) },
        { "BracketNesting", Repeat("![", 100) + Marker + Repeat("](b)", 100) },
        { "ParenthesisNesting", "[a](" + Repeat("(", 300) + Marker + Repeat(")", 300) + ")" },
        { "EmphasisNesting", Repeat("*a ", 200) + Marker + Repeat(" c*", 200) },
        { "EmphasisNesting", Repeat("**a ", 100) + Marker + Repeat(" c**", 100) },
        { "EmphasisNesting", Repeat("_a ", 200) + Marker + Repeat(" c_", 200) },
        { "EmphasisNesting", Repeat("~~a ", 100) + Marker + Repeat(" c~~", 100) },
        { "BacktickRun", Repeat("`", 100) + Marker }
    };

    [Theory]
    [MemberData(nameof(TooDeeplyNested))]
    public void Input_nested_beyond_the_limit_is_shown_as_escaped_text_without_parsing(string limit, string markdown)
    {
        var logger = new CapturingLogger<ReleaseNotesRenderer>();
        var renderer = new ReleaseNotesRenderer(logger);

        var html = renderer.RenderHtml(markdown);

        Assert.Equal(PlainText(markdown), html);
        var entry = Assert.Single(logger.Entries);
        Assert.Equal(LogLevel.Warning, entry.Level);
        Assert.Null(entry.Exception);
        Assert.Contains($"exceed the {limit} limit", entry.Message);
        Assert.DoesNotContain(Marker, entry.Message);
    }

    [Fact]
    public void Input_longer_than_the_limit_is_shown_as_escaped_text()
    {
        var markdown = Marker + " <b>x</b>\n\n" + new string('a', 200_000);
        var logger = new CapturingLogger<ReleaseNotesRenderer>();

        var html = new ReleaseNotesRenderer(logger).RenderHtml(markdown);

        Assert.Equal(PlainText(markdown), html);
        var entry = Assert.Single(logger.Entries);
        Assert.Contains("exceed the Length limit", entry.Message);
        Assert.Contains(markdown.Length.ToString(), entry.Message);
        Assert.DoesNotContain(Marker, entry.Message);
    }

    public static TheoryData<string, string, string> AllowedNesting() => new()
    {
        { "20 nested blockquotes", Repeat("> ", 20) + "deep", Repeat("<blockquote>\n", 20) + "<p>deep</p>" },
        { "8 nested lists", string.Concat(Enumerable.Range(0, 8).Select(i => new string(' ', i * 2) + "- level\n")), "<ul>\n<li>level\n<ul>\n<li>level" },
        { "60 nested closed spans", "x " + Repeat("<span>", 60) + "deep" + Repeat("</span>", 60), Repeat("<span>", 60) + "deep" + Repeat("</span>", 60) },
        { "40 nested closed divs", Repeat("<div>", 40) + "\ndeep\n" + Repeat("</div>", 40), Repeat("<div>", 40) },
        { "200 unclosed paragraphs", string.Concat(Enumerable.Range(0, 200).Select(i => $"<p>item {i}\n")), "<p>item 199" },
        { "200 unclosed list items", "<ul>\n" + string.Concat(Enumerable.Range(0, 200).Select(i => $"<li>item {i}\n")) + "</ul>", "<li>item 199" },
        { "200 table rows with implicit cell ends", "<table>\n" + string.Concat(Enumerable.Range(0, 200).Select(i => $"<tr><td>a{i}<td>b{i}\n")) + "</table>", "<td>b199" },
        { "300 list items with inline markup", string.Concat(Enumerable.Range(0, 300).Select(i => $"- **Breaking:** [#{i}](https://github.com/o/r/pull/{i}) needs ~1.2.{i} for snake_case_name (see docs\n")), "<strong>Breaking:</strong>" },
        { "200 generic types in one paragraph", Repeat("Returns Task<T> and `List<int>` or [IEnumerable<T>] ", 200), "Task&lt;T&gt;" },
        { "unbalanced brackets in separate paragraphs", Repeat("Fixed (see [notes\n\n", 200), "<p>Fixed (see [notes</p>" },
        { "deeply indented code in a fence", "```json\n" + string.Concat(Enumerable.Range(0, 100).Select(i => new string(' ', i * 2) + "[(*\n")) + "```", "<pre><code>[(*" },
        { "60 nested closed inline tags", "x " + Repeat("<b><i>", 30) + "deep" + Repeat("</i></b>", 30), Repeat("<b><i>", 30) + "deep" }
    };

    [Theory]
    [MemberData(nameof(AllowedNesting))]
    public void Deep_but_allowed_input_renders_as_markdown(string name, string markdown, string expected)
    {
        var logger = new CapturingLogger<ReleaseNotesRenderer>();

        var html = new ReleaseNotesRenderer(logger).RenderHtml(markdown);

        Assert.False(html == PlainText(markdown), $"{name} fell back to plain text");
        Assert.Contains(expected, html);
        Assert.Empty(logger.Entries);
    }

    [Fact]
    public void Long_input_under_the_limit_renders_as_markdown()
    {
        var builder = new StringBuilder();
        while (builder.Length < 120_000)
            builder.Append("## Fixes\n\n- **core:** handle `null` input in [parser](https://example.com/p) (#1234)\n\n");

        var html = new ReleaseNotesRenderer().RenderHtml(builder.ToString());

        Assert.StartsWith("<h2>Fixes</h2>", html);
        Assert.Contains("<strong>core:</strong>", html);
    }

    private static string PlainText(string markdown) => "<pre><code>" + WebUtility.HtmlEncode(markdown) + "</code></pre>";

    private static string Repeat(string value, int count) => string.Concat(Enumerable.Repeat(value, count));

    private static string Numbered(string format, int count) =>
        string.Concat(Enumerable.Range(0, count).Select(i => string.Format(format, i)));

    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, string Message, Exception? Exception)> Entries { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            Entries.Add((logLevel, formatter(state, exception), exception));
        }
    }
}
