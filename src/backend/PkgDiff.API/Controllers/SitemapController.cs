using Microsoft.AspNetCore.Mvc;
using PkgDiff.Application.Interfaces;

namespace PkgDiff.API.Controllers;

[ApiController]
[Route("api")]
public class SitemapController : ControllerBase
{
    private readonly IAdminPackageService _adminService;
    private readonly ILogger<SitemapController> _logger;

    public SitemapController(IAdminPackageService adminService, ILogger<SitemapController> logger)
    {
        _adminService = adminService;
        _logger = logger;
    }

    [HttpGet("sitemap")]
    [Produces("application/xml")]
    public async Task<IActionResult> GetDynamicSitemap()
    {
        try
        {
            var packages = await _adminService.GetPackagesForSitemapAsync();

            var xml = new System.Text.StringBuilder();
            xml.AppendLine("<?xml version=\"1.0\" encoding=\"UTF-8\"?>");
            xml.AppendLine("<urlset xmlns=\"http://www.sitemaps.org/schemas/sitemap/0.9\">");

            xml.AppendLine("  <url>");
            xml.AppendLine("    <loc>https://pkgdiff.dev/</loc>");
            xml.AppendLine("    <changefreq>daily</changefreq>");
            xml.AppendLine("    <priority>1.0</priority>");
            xml.AppendLine($"    <lastmod>{DateTime.UtcNow:yyyy-MM-dd}</lastmod>");
            xml.AppendLine("  </url>");

            xml.AppendLine("  <url>");
            xml.AppendLine("    <loc>https://pkgdiff.dev/packages</loc>");
            xml.AppendLine("    <changefreq>daily</changefreq>");
            xml.AppendLine("    <priority>0.9</priority>");
            xml.AppendLine($"    <lastmod>{DateTime.UtcNow:yyyy-MM-dd}</lastmod>");
            xml.AppendLine("  </url>");

            xml.AppendLine("  <url>");
            xml.AppendLine("    <loc>https://pkgdiff.dev/audit</loc>");
            xml.AppendLine("    <changefreq>monthly</changefreq>");
            xml.AppendLine("    <priority>0.8</priority>");
            xml.AppendLine("  </url>");


            xml.AppendLine("  <url>");
            xml.AppendLine("    <loc>https://pkgdiff.dev/terms</loc>");
            xml.AppendLine("    <changefreq>yearly</changefreq>");
            xml.AppendLine("    <priority>0.3</priority>");
            xml.AppendLine($"    <lastmod>{DateTime.UtcNow:yyyy-MM-dd}</lastmod>");
            xml.AppendLine("  </url>");

            xml.AppendLine("  <url>");
            xml.AppendLine("    <loc>https://pkgdiff.dev/privacy</loc>");
            xml.AppendLine("    <changefreq>yearly</changefreq>");
            xml.AppendLine("    <priority>0.3</priority>");
            xml.AppendLine($"    <lastmod>{DateTime.UtcNow:yyyy-MM-dd}</lastmod>");
            xml.AppendLine("  </url>");

            xml.AppendLine("  <url>");
            xml.AppendLine("    <loc>https://pkgdiff.dev/cookies</loc>");
            xml.AppendLine("    <changefreq>yearly</changefreq>");
            xml.AppendLine("    <priority>0.3</priority>");
            xml.AppendLine($"    <lastmod>{DateTime.UtcNow:yyyy-MM-dd}</lastmod>");
            xml.AppendLine("  </url>");

            foreach (var package in packages)
            {
                var encodedPackageId = Uri.EscapeDataString(package.PackageId);
                xml.AppendLine("  <url>");
                xml.AppendLine($"    <loc>https://pkgdiff.dev/package/{package.Registry}/{encodedPackageId}</loc>");
                xml.AppendLine($"    <changefreq>{package.ChangeFreq}</changefreq>");
                xml.AppendLine($"    <priority>{package.Priority:0.0}</priority>");
                if (package.LastModified.HasValue)
                {
                    xml.AppendLine($"    <lastmod>{package.LastModified.Value:yyyy-MM-dd}</lastmod>");
                }
                xml.AppendLine("  </url>");
            }

            xml.AppendLine("</urlset>");

            return Content(xml.ToString(), "application/xml");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error generating dynamic sitemap");
            return StatusCode(500, new { error = "An error occurred while generating sitemap" });
        }
    }
}
