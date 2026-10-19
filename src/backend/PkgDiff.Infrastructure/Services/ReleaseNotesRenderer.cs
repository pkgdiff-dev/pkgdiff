using System.Net;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using Ganss.Xss;
using Markdig;
using Markdig.Extensions.EmphasisExtras;
using Markdig.Extensions.Tables;
using Markdig.Extensions.TaskLists;
using Markdig.Renderers;
using Markdig.Renderers.Html;
using Markdig.Syntax;
using Markdig.Syntax.Inlines;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using PkgDiff.Application.Interfaces;

namespace PkgDiff.Infrastructure.Services;

// Markdown is parsed once, here, and only the sanitized HTML leaves the server: no second parser can
// disagree with this one about what is code and what is markup.
public sealed class ReleaseNotesRenderer : IReleaseNotesRenderer, IDisposable
{
    private const string LinkRel = "noopener noreferrer nofollow";

    private static readonly string[] AllowedTags =
    [
        "a", "p", "br", "b", "strong", "i", "em", "code", "pre", "ul", "ol", "li",
        "h1", "h2", "h3", "h4", "h5", "h6", "blockquote", "table", "thead", "tbody", "tfoot", "tr", "th", "td",
        "img", "hr", "del", "s", "ins", "sup", "sub", "kbd", "details", "summary", "div", "span"
    ];

    private static readonly string[] AllowedAttributes =
        ["href", "src", "alt", "title", "open", "align", "start", "colspan", "rowspan", "width", "height"];

    private static readonly string[] AllowedSchemes = ["http", "https", "mailto"];

    // An image loads from where the package author pointed it, telling that host who reads the notes.
    // These hosts are trusted with that; an image anywhere else is shown as a link the reader may open.
    private static readonly string[] TrustedImageHosts = ["github.com", "githubusercontent.com", "img.shields.io", "badgen.net"];

    internal static readonly HashSet<string> AllowedTagSet = new(AllowedTags, StringComparer.OrdinalIgnoreCase);

    // Active or raw-text elements: removed together with everything up to their closing tag
    private static readonly HashSet<string> DroppedWithContent = new(StringComparer.OrdinalIgnoreCase)
    {
        "script", "style", "textarea", "iframe", "frame", "frameset", "object", "embed", "applet",
        "noscript", "noembed", "noframes", "template", "xmp", "plaintext", "title", "svg", "math"
    };

    // Markdig's own depth limit is the second line of defence behind MarkdownComplexityGuard
    private static readonly MarkdownPipeline Pipeline =
        new MarkdownPipelineBuilder { MaximumNestingDepth = MarkdownComplexityGuard.MaxDepth }
        .UseAutoLinks()
        .UsePipeTables()
        .UseTaskLists()
        .UseEmphasisExtras(EmphasisExtraOptions.Strikethrough)
        .Build();

    // CommonMark raw HTML shapes; anything else starting with '<' is plain text
    private static readonly Regex HtmlTokenRegex = new(
        @"(?<comment><!--(?:-?>|[\s\S]*?-->))"
        + @"|(?<other><\?[\s\S]*?\?>|<![A-Za-z][^>]*>|<!\[CDATA\[[\s\S]*?\]\]>)"
        + @"|(?<close></(?<closeName>[A-Za-z][A-Za-z0-9-]*)\s*>)"
        + @"|(?<open><(?<openName>[A-Za-z][A-Za-z0-9-]*)(?:\s+[A-Za-z_:][A-Za-z0-9_.:-]*(?:\s*=\s*(?:[^\s""'=<>`]+|'[^']*'|""[^""]*""))?)*\s*/?>)",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    // Bounded by the total length of the cached HTML (about 16 MB of UTF-16)
    private const long CacheSizeLimitChars = 8_000_000;

    private static readonly TimeSpan CacheSlidingExpiration = TimeSpan.FromHours(6);

    private readonly HtmlSanitizer _sanitizer;
    private readonly MemoryCache _cache = new(new MemoryCacheOptions { SizeLimit = CacheSizeLimitChars });
    private readonly ILogger<ReleaseNotesRenderer> _logger;

    public ReleaseNotesRenderer(ILogger<ReleaseNotesRenderer>? logger = null)
    {
        _logger = logger ?? NullLogger<ReleaseNotesRenderer>.Instance;
        _sanitizer = new HtmlSanitizer();

        _sanitizer.AllowedTags.Clear();
        foreach (var tag in AllowedTags)
            _sanitizer.AllowedTags.Add(tag);

        _sanitizer.AllowedAttributes.Clear();
        foreach (var attribute in AllowedAttributes)
            _sanitizer.AllowedAttributes.Add(attribute);

        _sanitizer.AllowedSchemes.Clear();
        foreach (var scheme in AllowedSchemes)
            _sanitizer.AllowedSchemes.Add(scheme);

        _sanitizer.AllowedCssProperties.Clear();
        _sanitizer.AllowedAtRules.Clear();
        _sanitizer.AllowedClasses.Clear();
        _sanitizer.AllowDataAttributes = false;

        // Images may only load from the web, never from relative or mail URLs
        _sanitizer.FilterUrl += (_, e) =>
        {
            if (string.Equals(e.Tag.NodeName, "IMG", StringComparison.OrdinalIgnoreCase)
                && e.SanitizedUrl is not null
                && !IsAbsoluteHttpUrl(e.SanitizedUrl))
            {
                e.SanitizedUrl = null;
            }
        };

        _sanitizer.PostProcessNode += (_, e) =>
        {
            if (e.Node is IElement element && string.Equals(element.NodeName, "A", StringComparison.OrdinalIgnoreCase))
            {
                element.SetAttribute("rel", LinkRel);
                element.RemoveAttribute("target");
            }
            else if (e.Node is IElement image && string.Equals(image.NodeName, "IMG", StringComparison.OrdinalIgnoreCase)
                     && image.GetAttribute("src") is { Length: > 0 } src)
            {
                if (IsTrustedImageHost(src))
                {
                    image.SetAttribute("referrerpolicy", "no-referrer");
                    image.SetAttribute("loading", "lazy");
                    return;
                }

                var link = e.Document.CreateElement("a");
                link.SetAttribute("href", src);
                link.SetAttribute("rel", LinkRel);
                var alt = image.GetAttribute("alt");
                link.TextContent = string.IsNullOrWhiteSpace(alt) ? "[image]" : $"[image: {alt}]";
                e.ReplacementNodes.Add(link);
            }
        };
    }

    private static bool IsTrustedImageHost(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && TrustedImageHosts.Any(host => uri.Host.Equals(host, StringComparison.OrdinalIgnoreCase)
                                         || uri.Host.EndsWith("." + host, StringComparison.OrdinalIgnoreCase));

    public string RenderHtml(string markdown)
    {
        if (string.IsNullOrWhiteSpace(markdown))
            return string.Empty;

        // Rendering costs milliseconds per note and a package page renders every cached note, so identical
        // input is served from a size-bounded cache keyed by its hash
        var key = Convert.ToHexString(SHA256.HashData(MemoryMarshal.AsBytes(markdown.AsSpan())));
        if (_cache.TryGetValue(key, out string? cached) && cached is not null)
            return cached;

        string html;
        var limit = MarkdownComplexityGuard.Check(markdown, out var measured);
        if (limit != MarkdownComplexityGuard.Limit.None)
        {
            // A stack overflow in the parsers ends the process and no catch block can stop it, so input
            // that could cause one is never parsed
            _logger.LogWarning(
                "Release notes exceed the {Limit} limit ({Measured}) at {Length} characters, showing them as plain text",
                limit, measured, markdown.Length);
            html = PlainText(markdown);
        }
        else
        {
            try
            {
                html = Render(markdown);
            }
            catch (Exception ex)
            {
                // Markdig rejects pathologically nested input; one hostile note must not fail the whole page
                _logger.LogWarning(ex, "Release notes could not be rendered as markdown, showing them as plain text");
                html = PlainText(markdown);
            }
        }

        _cache.Set(key, html, new MemoryCacheEntryOptions
        {
            Size = Math.Max(1, html.Length),
            SlidingExpiration = CacheSlidingExpiration
        });

        return html;
    }

    public void Dispose() => _cache.Dispose();

    private static string PlainText(string markdown) => "<pre><code>" + WebUtility.HtmlEncode(markdown) + "</code></pre>";

    private string Render(string markdown)
    {
        var document = Markdown.Parse(markdown, Pipeline);
        RewriteInlineHtml(document);
        ReplaceTaskListCheckboxes(document);
        UnescapeTableCellPipes(document);

        using var writer = new StringWriter();
        var renderer = new HtmlRenderer(writer) { MaximumNestingDepth = MarkdownComplexityGuard.MaxDepth };
        Pipeline.Setup(renderer);
        renderer.ObjectRenderers.ReplaceOrAdd<HtmlBlockRenderer>(new AllowlistedHtmlBlockRenderer());
        renderer.Render(document);
        writer.Flush();

        return _sanitizer.Sanitize(writer.ToString()).Trim();
    }

    private static bool IsAbsoluteHttpUrl(string url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps);

    private static void RewriteInlineHtml(MarkdownDocument document)
    {
        foreach (var html in document.Descendants<HtmlInline>().ToList())
        {
            // Already removed as the content of an earlier dropped element
            if (html.Parent is null)
                continue;

            var tag = html.Tag ?? string.Empty;
            var match = HtmlTokenRegex.Match(tag);
            if (!match.Success || match.Length != tag.Length)
            {
                html.ReplaceBy(new LiteralInline(tag));
                continue;
            }

            if (match.Groups["comment"].Success)
            {
                html.Remove();
                continue;
            }

            if (match.Groups["open"].Success)
            {
                var name = match.Groups["openName"].Value;
                if (DroppedWithContent.Contains(name))
                {
                    var closing = FindClosingSibling(html, name);
                    if (closing is not null)
                    {
                        RemoveRange(html, closing);
                        continue;
                    }
                }
                else if (AllowedTagSet.Contains(name))
                {
                    continue;
                }
            }
            else if (match.Groups["close"].Success && AllowedTagSet.Contains(match.Groups["closeName"].Value))
            {
                continue;
            }

            // Not allowlisted: the tag stays visible as text (e.g. the <T> of List<T>)
            html.ReplaceBy(new LiteralInline(tag));
        }
    }

    private static HtmlInline? FindClosingSibling(HtmlInline open, string name)
    {
        for (var node = open.NextSibling; node is not null; node = node.NextSibling)
        {
            if (node is not HtmlInline candidate)
                continue;

            var match = HtmlTokenRegex.Match(candidate.Tag ?? string.Empty);
            if (match.Success
                && match.Groups["close"].Success
                && string.Equals(match.Groups["closeName"].Value, name, StringComparison.OrdinalIgnoreCase))
            {
                return candidate;
            }
        }

        return null;
    }

    private static void RemoveRange(Inline first, Inline last)
    {
        var node = first;
        while (node is not null)
        {
            var next = node.NextSibling;
            node.Remove();
            if (ReferenceEquals(node, last))
                break;
            node = next;
        }
    }

    private static void ReplaceTaskListCheckboxes(MarkdownDocument document)
    {
        foreach (var task in document.Descendants<TaskList>().ToList())
            task.ReplaceBy(new LiteralInline(task.Checked ? "☑" : "☐"));
    }

    // GFM: inside a table cell "\|" is how a code span carries a literal pipe
    private static void UnescapeTableCellPipes(MarkdownDocument document)
    {
        foreach (var cell in document.Descendants<TableCell>())
        {
            foreach (var code in cell.Descendants<CodeInline>())
            {
                if (code.Content.Contains("\\|", StringComparison.Ordinal))
                    code.Content = code.Content.Replace("\\|", "|", StringComparison.Ordinal);
            }
        }
    }

    // Applies the same allowlist to raw HTML blocks, token by token
    internal static string RewriteHtmlFragment(string raw)
    {
        var tokens = HtmlTokenRegex.Matches(raw);
        var builder = new StringBuilder(raw.Length);
        var position = 0;

        for (var i = 0; i < tokens.Count; i++)
        {
            var token = tokens[i];
            builder.Append(EscapeText(raw.AsSpan(position, token.Index - position)));
            position = token.Index + token.Length;

            if (token.Groups["comment"].Success)
                continue;

            if (token.Groups["open"].Success)
            {
                var name = token.Groups["openName"].Value;
                if (DroppedWithContent.Contains(name))
                {
                    var closing = FindClosingToken(tokens, i + 1, name);
                    if (closing >= 0)
                    {
                        position = tokens[closing].Index + tokens[closing].Length;
                        i = closing;
                        continue;
                    }
                }
                else if (AllowedTagSet.Contains(name))
                {
                    builder.Append(token.Value);
                    continue;
                }
            }
            else if (token.Groups["close"].Success && AllowedTagSet.Contains(token.Groups["closeName"].Value))
            {
                builder.Append(token.Value);
                continue;
            }

            builder.Append(WebUtility.HtmlEncode(token.Value));
        }

        builder.Append(EscapeText(raw.AsSpan(position)));
        return builder.ToString();
    }

    private static int FindClosingToken(MatchCollection tokens, int start, string name)
    {
        for (var j = start; j < tokens.Count; j++)
        {
            if (tokens[j].Groups["close"].Success
                && string.Equals(tokens[j].Groups["closeName"].Value, name, StringComparison.OrdinalIgnoreCase))
            {
                return j;
            }
        }

        return -1;
    }

    // Text between tags of a raw HTML block keeps its entities, but a stray '<' can never open a tag
    private static string EscapeText(ReadOnlySpan<char> text) => text.ToString().Replace("<", "&lt;");

    private sealed class AllowlistedHtmlBlockRenderer : HtmlObjectRenderer<HtmlBlock>
    {
        protected override void Write(HtmlRenderer renderer, HtmlBlock obj)
        {
            renderer.Write(RewriteHtmlFragment(obj.Lines.ToString()));
            renderer.EnsureLine();
        }
    }
}
