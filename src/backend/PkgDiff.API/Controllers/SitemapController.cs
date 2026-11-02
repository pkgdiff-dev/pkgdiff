using System.Security;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Caching.Memory;
using PkgDiff.Application.Interfaces;

namespace PkgDiff.API.Controllers;

[ApiController]
[Route("api")]
public class SitemapController : ControllerBase
{
    private const string BaseUrl = "https://pkgdiff.dev";
    private const string CacheKey = "sitemap-xml";

    private static readonly string[] StaticPaths =
    {
        "/",
        "/packages",
        "/audit",
        "/about",
        "/contributors",
        "/terms",
        "/privacy",
        "/cookies",
    };

    // The values used in public URLs, where pub.dev is "pub"
    private static readonly string[] RegistryHubs = { "nuget", "npm", "pypi", "pub", "packagist", "maven" };

    private readonly IAdminPackageService _adminService;
    private readonly IMemoryCache _memory;
    private readonly ILogger<SitemapController> _logger;

    public SitemapController(
        IAdminPackageService adminService,
        IMemoryCache memory,
        ILogger<SitemapController> logger)
    {
        _adminService = adminService;
        _memory = memory;
        _logger = logger;
    }

    [HttpGet("sitemap")]
    [Produces("application/xml")]
    public async Task<IActionResult> GetDynamicSitemap()
    {
        try
        {
            if (!_memory.TryGetValue(CacheKey, out string? sitemap) || sitemap is null)
            {
                sitemap = await BuildSitemapAsync();
                _memory.Set(CacheKey, sitemap, TimeSpan.FromHours(1));
            }

            return Content(sitemap, "application/xml");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating dynamic sitemap");
            return StatusCode(500, new { error = "An error occurred while generating sitemap" });
        }
    }

    private async Task<string> BuildSitemapAsync()
    {
        var packages = await _adminService.GetPackagesForSitemapAsync();

        var xml = new System.Text.StringBuilder();
        xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
        xml.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");

        foreach (var path in StaticPaths)
        {
            AppendUrl(xml, path, null);
        }

        foreach (var registry in RegistryHubs)
        {
            AppendUrl(xml, $"/packages?registry={registry}", null);
        }

        foreach (var package in packages)
        {
            AppendUrl(xml, $"/package/{package.Registry}/{package.PackageId}", package.LastModified);
        }

        xml.AppendLine("</urlset>");
        return xml.ToString();
    }

    // Must match the page's canonical URL, so the path is written as is and only escaped for XML
    private static void AppendUrl(System.Text.StringBuilder xml, string path, DateTime? lastModified)
    {
        xml.AppendLine("  <url>");
        xml.AppendLine($"    <loc>{SecurityElement.Escape(BaseUrl + path)}</loc>");
        if (lastModified.HasValue)
        {
            xml.AppendLine($"    <lastmod>{lastModified.Value:yyyy-MM-dd}</lastmod>");
        }
        xml.AppendLine("  </url>");
    }
}
