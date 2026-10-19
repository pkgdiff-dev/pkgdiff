using System.Text.Json;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using PkgDiff.Infrastructure.Services;

namespace PkgDiff.Tests.Content;

public class ReleaseNotesRendererTests
{
    private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
    {
        "a", "p", "br", "b", "strong", "i", "em", "code", "pre", "ul", "ol", "li",
        "h1", "h2", "h3", "h4", "h5", "h6", "blockquote", "table", "thead", "tbody", "tfoot", "tr", "th", "td",
        "img", "hr", "del", "s", "ins", "sup", "sub", "kbd", "details", "summary", "div", "span"
    };

    private static readonly string[] AllowedSchemes = ["http", "https", "mailto"];

    private static readonly Regex SchemeRegex = new("^([a-z][a-z0-9+.-]*):", RegexOptions.Compiled);

    private readonly ReleaseNotesRenderer _renderer = new();

    public static TheoryData<string> Payloads()
    {
        var data = new TheoryData<string>();
        foreach (var file in new[] { "markdown-xss-payloads-1.json", "markdown-xss-payloads-2.json" })
        {
            var path = Path.Combine(AppContext.BaseDirectory, "Content", "TestData", file);
            foreach (var payload in JsonSerializer.Deserialize<List<string>>(File.ReadAllText(path))!)
                data.Add(payload);
        }

        return data;
    }

    [Fact]
    public void Payload_corpus_is_complete()
    {
        Assert.Equal(208, Payloads().Count);
    }

    [Theory]
    [MemberData(nameof(Payloads))]
    public void Payload_renders_to_inert_html(string markdown)
    {
        var html = _renderer.RenderHtml(markdown);

        AssertInert(html);
    }

    [Theory]
    [MemberData(nameof(Payloads))]
    public void Payload_rendering_is_deterministic(string markdown)
    {
        var first = new ReleaseNotesRenderer().RenderHtml(markdown);
        var second = new ReleaseNotesRenderer().RenderHtml(markdown);

        Assert.Equal(first, second);
        Assert.Equal(first, _renderer.RenderHtml(markdown));
        Assert.Equal(first, _renderer.RenderHtml(markdown));
    }

    [Theory]
    [InlineData("https://ok.com/`<img src=x onerror=alert(1)>`")]
    [InlineData("www.ok.com/`<img src=x onerror=alert(1)>`")]
    [InlineData("see http://a.b`<svg onload=alert(1)>`")]
    [InlineData("https://a.b/`<a href=\"javascript:alert(1)\">x</a>`")]
    [InlineData("www.a.b/`<details open ontoggle=alert(1)>`")]
    [InlineData("| a |\n|---|\n| https://a.b/`<img src=x onerror=alert(1)>` |")]
    [InlineData("- `a\n<img src=x onerror=alert(1)>`")]
    [InlineData("~~`~~<img src=x onerror=alert(1)>`")]
    [InlineData("~`~<img src=x onerror=alert(1)>`")]
    public void Review_cr01_bypasses_render_inert(string markdown)
    {
        AssertInert(_renderer.RenderHtml(markdown));
    }

    [Theory]
    [InlineData("<script>alert(1)</script>ok")]
    [InlineData("<svg onload=alert(1)>")]
    [InlineData("<iframe src=//evil>")]
    [InlineData("<object data=x></object><embed src=x>")]
    [InlineData("<a href=\"javascript:alert(1)\">a</a>")]
    [InlineData("[x](javascript:alert(1))")]
    [InlineData("[x](java&#115;cript:alert(1))")]
    [InlineData("[x](data:text/html;base64,PHNjcmlwdD5hbGVydCgxKTwvc2NyaXB0Pg==)")]
    [InlineData("<p style=\"color:red\" onclick=\"x\">x</p>")]
    public void Active_content_renders_inert(string markdown)
    {
        AssertInert(_renderer.RenderHtml(markdown));
    }

    [Fact]
    public void Script_and_its_content_are_dropped()
    {
        var html = _renderer.RenderHtml("a <script>alert(1)</script> b\n\n<style>\np { color: red }\n</style>\n\nafter");

        Assert.DoesNotContain("alert", html);
        Assert.DoesNotContain("color", html);
        Assert.Contains("after", html);
    }

    [Fact]
    public void Textarea_and_iframe_are_dropped_with_their_content()
    {
        var html = _renderer.RenderHtml("x <textarea>secret</textarea> y <iframe src=\"https://e\">inner</iframe> z");

        Assert.DoesNotContain("secret", html);
        Assert.DoesNotContain("inner", html);
        Assert.Contains("x", html);
        Assert.Contains("z", html);
    }

    [Fact]
    public void Unclosed_dropped_tag_stays_visible_as_text()
    {
        var html = _renderer.RenderHtml("Fixed <textarea> autofocus");

        Assert.Equal("<p>Fixed &lt;textarea&gt; autofocus</p>", html);
    }

    [Fact]
    public void Generics_in_text_stay_visible()
    {
        var html = _renderer.RenderHtml("Use List<T>, Task<string> and Dictionary<K,V>.");

        Assert.Equal("<p>Use List&lt;T&gt;, Task&lt;string&gt; and Dictionary&lt;K,V&gt;.</p>", html);
    }

    [Fact]
    public void Unknown_raw_html_is_shown_as_text()
    {
        var html = _renderer.RenderHtml("<custom-tag a=\"1\">x</custom-tag>");

        Assert.Contains("&lt;custom-tag a=\"1\"&gt;x&lt;/custom-tag&gt;", html);
    }

    [Fact]
    public void Fenced_code_block_keeps_its_text()
    {
        var html = _renderer.RenderHtml("```csharp\nvar x = new List<int>(); // <script> & more\n```");

        Assert.Equal("<pre><code>var x = new List&lt;int&gt;(); // &lt;script&gt; &amp; more\n</code></pre>", html);
    }

    [Fact]
    public void Indented_code_block_keeps_its_text()
    {
        var html = _renderer.RenderHtml("Indented:\n\n    List<T> x = new();\n    <div class=\"x\">hello</div>");

        Assert.Contains("<pre><code>List&lt;T&gt; x = new();\n&lt;div class=\"x\"&gt;hello&lt;/div&gt;\n</code></pre>", html);
        Assert.DoesNotContain("&amp;lt;", html);
    }

    [Fact]
    public void Inline_code_keeps_its_text()
    {
        var html = _renderer.RenderHtml("Inline `Dictionary<K,V> && <img src=x onerror=alert(1)>` here");

        Assert.Equal("<p>Inline <code>Dictionary&lt;K,V&gt; &amp;&amp; &lt;img src=x onerror=alert(1)&gt;</code> here</p>", html);
    }

    [Fact]
    public void Table_code_span_with_escaped_pipe_shows_the_pipe()
    {
        var html = _renderer.RenderHtml("| a | b |\n|---|---|\n| `x\\|<T>` | y |");

        Assert.Contains("<td><code>x|&lt;T&gt;</code></td>", html);
        Assert.Contains("<td>y</td>", html);
        Assert.Contains("<th>a</th>", html);
    }

    [Fact]
    public void Links_are_kept_with_a_safe_rel_and_no_target()
    {
        var html = _renderer.RenderHtml("[docs](https://example.com/a?b=1&c=2) <a href=\"https://x.test\" target=\"_blank\">x</a>");

        Assert.Contains("<a href=\"https://example.com/a?b=1&amp;c=2\" rel=\"noopener noreferrer nofollow\">docs</a>", html);
        Assert.Contains("<a href=\"https://x.test\" rel=\"noopener noreferrer nofollow\">x</a>", html);
        Assert.DoesNotContain("target", html);
    }

    [Fact]
    public void Bare_urls_become_links()
    {
        var html = _renderer.RenderHtml("See https://github.com/o/r/pull/1 and www.example.com or <mailto:a@b.co>.");

        Assert.Contains("<a href=\"https://github.com/o/r/pull/1\" rel=\"noopener noreferrer nofollow\">https://github.com/o/r/pull/1</a>", html);
        Assert.Contains("<a href=\"http://www.example.com\" rel=\"noopener noreferrer nofollow\">www.example.com</a>", html);
        Assert.Contains("<a href=\"mailto:a@b.co\" rel=\"noopener noreferrer nofollow\">mailto:a@b.co</a>", html);
    }

    [Fact]
    public void Relative_links_are_kept()
    {
        var html = _renderer.RenderHtml("[changelog](../CHANGELOG.md#v2)");

        Assert.Contains("href=\"../CHANGELOG.md#v2\"", html);
    }

    [Fact]
    public void Script_scheme_links_lose_their_href()
    {
        var html = _renderer.RenderHtml("[x](javascript:alert(1)) [y](vbscript:msgbox)");

        Assert.Equal("<p><a rel=\"noopener noreferrer nofollow\">x</a> <a rel=\"noopener noreferrer nofollow\">y</a></p>", html);
    }

    [Fact]
    public void Images_only_load_over_http_or_https()
    {
        var html = _renderer.RenderHtml("![a](https://img.test/a.png) ![b](b.png) ![c](javascript:alert(1)) ![d](mailto:a@b.co)");

        Assert.Contains("<a href=\"https://img.test/a.png\" rel=\"noopener noreferrer nofollow\">[image: a]</a>", html);
        Assert.Contains("<img alt=\"b\">", html);
        Assert.Contains("<img alt=\"c\">", html);
        Assert.Contains("<img alt=\"d\">", html);
    }

    [Fact]
    public void Task_list_checkboxes_become_text()
    {
        var html = _renderer.RenderHtml("- [x] done\n- [ ] todo");

        Assert.Equal("<ul>\n<li>☑ done</li>\n<li>☐ todo</li>\n</ul>", html);
        Assert.DoesNotContain("<input", html);
    }

    [Fact]
    public void Strikethrough_renders_but_plus_signs_stay_text()
    {
        var html = _renderer.RenderHtml("~~gone~~ C++ and C++ a~b~");

        Assert.Equal("<p><del>gone</del> C++ and C++ a~b~</p>", html);
    }

    [Fact]
    public void Details_and_summary_are_kept()
    {
        var html = _renderer.RenderHtml("<details>\n<summary>More</summary>\n\n- item `List<T>`\n</details>");

        Assert.Contains("<details>", html);
        Assert.Contains("<summary>More</summary>", html);
        Assert.Contains("<li>item <code>List&lt;T&gt;</code></li>", html);
    }

    [Fact]
    public void Html_comments_are_dropped()
    {
        var html = _renderer.RenderHtml("<!-- hidden -->visible <!-- also hidden -->");

        Assert.DoesNotContain("hidden", html);
        Assert.Contains("visible", html);
    }

    [Fact]
    public void Style_and_event_attributes_are_removed_from_allowed_tags()
    {
        var html = _renderer.RenderHtml("<p align=\"center\" style=\"color:red\" onclick=\"x\" class=\"c\"><img src=\"https://raw.githubusercontent.com/o/r/y.png\" width=\"100\" onerror=\"x\"></p>");

        Assert.Equal("<p align=\"center\"><img src=\"https://raw.githubusercontent.com/o/r/y.png\" width=\"100\" referrerpolicy=\"no-referrer\" loading=\"lazy\"></p>", html);
    }

    [Fact]
    public void Ampersands_and_blockquotes_render_normally()
    {
        var html = _renderer.RenderHtml("> a && b &amp; c");

        Assert.Equal("<blockquote>\n<p>a &amp;&amp; b &amp; c</p>\n</blockquote>", html);
    }

    [Fact]
    public void Too_deeply_nested_input_falls_back_to_escaped_text()
    {
        var markdown = string.Concat(Enumerable.Repeat("> ", 200)) + "<img src=x onerror=alert(1)>";

        var html = _renderer.RenderHtml(markdown);

        Assert.StartsWith("<pre><code>&gt; &gt; ", html);
        Assert.Contains("&lt;img src=x onerror=alert(1)&gt;", html);
        AssertInert(html);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   \n\t")]
    public void Empty_input_renders_empty(string? markdown)
    {
        Assert.Equal(string.Empty, _renderer.RenderHtml(markdown!));
    }

    private static void AssertInert(string html)
    {
        var document = new HtmlParser().ParseDocument("<!DOCTYPE html><html><body>" + html + "</body></html>");
        var body = document.Body!;

        foreach (var element in body.QuerySelectorAll("*"))
        {
            Assert.True(AllowedTags.Contains(element.LocalName), $"Disallowed <{element.LocalName}> in: {html}");

            foreach (var attribute in element.Attributes)
            {
                Assert.False(attribute.Name.StartsWith("on", StringComparison.OrdinalIgnoreCase), $"Event handler {attribute.Name} in: {html}");
                Assert.NotEqual("style", attribute.Name, StringComparer.OrdinalIgnoreCase);

                if (attribute.Name is "href" or "src")
                    AssertSafeUrl(element, attribute.Value, html);
            }
        }

        Assert.DoesNotMatch(new Regex(@"<\s*(script|iframe|object|embed|svg|style|textarea|math)\b", RegexOptions.IgnoreCase), html);
    }

    private static void AssertSafeUrl(IElement element, string value, string html)
    {
        // Browsers ignore ASCII whitespace and control characters when reading a scheme
        var cleaned = new string(value.Where(c => c > ' ' && c != '\u007F').ToArray()).ToLowerInvariant();
        var scheme = SchemeRegex.Match(cleaned);

        if (element.LocalName == "img")
        {
            Assert.True(cleaned.StartsWith("http://") || cleaned.StartsWith("https://"), $"Image source {value} in: {html}");
            return;
        }

        if (scheme.Success)
            Assert.True(AllowedSchemes.Contains(scheme.Groups[1].Value), $"Scheme {value} in: {html}");
    }
}
