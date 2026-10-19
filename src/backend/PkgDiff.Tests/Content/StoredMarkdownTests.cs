using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using PkgDiff.API.Extensions;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Services;
using PkgDiff.Infrastructure.Services;

namespace PkgDiff.Tests.Content;

public class StoredMarkdownTests
{
    [Theory]
    [InlineData("Indented:\n\n    List<T> x = new();\n    <div class=\"x\">hello</div>")]
    [InlineData("| a | b |\n|---|---|\n| `x\\|<img src=x onerror=alert(1)>` | y |")]
    [InlineData("Use List<T> & Task<string> &amp; &lt;b&gt;")]
    [InlineData("```csharp\nvar x = new List<int>(); // <script>\n```")]
    [InlineData("<img src=x onerror=alert(1)> [x](javascript:alert(1))")]
    [InlineData("tab\tand\r\nwindows line")]
    public void Markdown_is_stored_exactly_as_obtained(string markdown)
    {
        Assert.Equal(markdown, StoredMarkdown.Normalize(markdown));
    }

    [Fact]
    public void Control_characters_are_stripped()
    {
        Assert.Equal("abc\td\ne\rf", StoredMarkdown.Normalize("a\0b\u0007c\td\ne\rf\u001B\u007F\u0085"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    public void Missing_markdown_is_stored_empty(string? markdown)
    {
        Assert.Equal(string.Empty, StoredMarkdown.Normalize(markdown));
    }

    [Theory]
    [InlineData("    List<T> x = new();", "<pre><code>List&lt;T&gt; x = new();\n</code></pre>")]
    [InlineData("| a |\n|---|\n| `x\\|<T>` |", "<td><code>x|&lt;T&gt;</code></td>")]
    public void Stored_code_renders_unchanged_however_often_it_is_stored(string markdown, string expected)
    {
        var renderer = new ReleaseNotesRenderer();
        var once = StoredMarkdown.Normalize(markdown);
        var twice = StoredMarkdown.Normalize(once);

        Assert.Equal(once, twice);
        Assert.Contains(expected, renderer.RenderHtml(twice));
        Assert.DoesNotContain("&amp;lt;", renderer.RenderHtml(twice));
    }

    [Fact]
    public void No_release_note_rewrite_job_is_left()
    {
        var assemblies = new[]
        {
            typeof(IReleaseNotesRenderer).Assembly,
            typeof(ReleaseNotesRenderer).Assembly,
            typeof(ServiceCollectionExtensions).Assembly
        };

        Assert.DoesNotContain(assemblies.SelectMany(a => a.GetTypes()), t => t.Name.Contains("Resanitize", StringComparison.Ordinal));
        Assert.DoesNotContain(assemblies.SelectMany(a => a.GetTypes()), t => t.Name == "MarkdownSanitizer");
    }

    [Fact]
    public void Business_services_register_no_startup_maintenance_and_a_single_renderer()
    {
        var services = new ServiceCollection().AddBusinessServices();

        Assert.DoesNotContain(services, d => d.ServiceType == typeof(IHostedService));
        var renderer = Assert.Single(services, d => d.ServiceType == typeof(IReleaseNotesRenderer));
        Assert.Equal(ServiceLifetime.Singleton, renderer.Lifetime);
    }
}
