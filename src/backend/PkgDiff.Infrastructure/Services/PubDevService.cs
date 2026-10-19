using HtmlAgilityPack;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using PkgDiff.Application.Interfaces;
using PkgDiff.Shared.DTOs;
using PkgDiff.Shared.Validation;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace PkgDiff.Infrastructure.Services;

public class PubDevService : IPubDevService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<PubDevService> _logger;
    private readonly string _baseUrl = "https://pub.dev/packages";

    public PubDevService(HttpClient httpClient, ILogger<PubDevService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<string> GetReleaseNotesAsync(string packageId, string version)
    {
        try
        {
            var url = $"{_baseUrl}/{PackageUrlEncoder.EscapeSegment(packageId)}/changelog";
            var html = await _httpClient.GetStringAsync(url);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var changelogNodes = doc.DocumentNode.SelectNodes("//div[@class='changelog-entry']");
            if (changelogNodes == null || !changelogNodes.Any())
                return string.Empty;

            var versionId = version.Replace(".", ""); // pub.dev changelog anchors drop the dots

            foreach (var node in changelogNodes)
            {
                var headerNode = node.SelectSingleNode(".//h2");
                if (headerNode != null && headerNode.InnerText.Trim().StartsWith(version))
                {
                    var contentNode = node.SelectSingleNode(".//div[@class='changelog-content']");
                    if (contentNode == null) return string.Empty;

                    var rawHtml = contentNode.InnerHtml;

                    rawHtml = Regex.Replace(rawHtml, "<br[^>]*>", "\n", RegexOptions.IgnoreCase);
                    rawHtml = Regex.Replace(rawHtml, "</p>", "\n\n", RegexOptions.IgnoreCase);
                    rawHtml = Regex.Replace(rawHtml, "<li[^>]*>", "\n- ", RegexOptions.IgnoreCase);
                    rawHtml = Regex.Replace(rawHtml, "<h[1-6][^>]*>", "\n# ", RegexOptions.IgnoreCase);

                    rawHtml = Regex.Replace(rawHtml, "<strong[^>]*>(.*?)</strong>", "**$1**", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    rawHtml = Regex.Replace(rawHtml, "<b[^>]*>(.*?)</b>", "**$1**", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    rawHtml = Regex.Replace(rawHtml, "<em[^>]*>(.*?)</em>", "*$1*", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    rawHtml = Regex.Replace(rawHtml, "<i[^>]*>(.*?)</i>", "*$1*", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    rawHtml = Regex.Replace(rawHtml, "<code[^>]*>(.*?)</code>", "`$1`", RegexOptions.IgnoreCase | RegexOptions.Singleline);
                    rawHtml = Regex.Replace(rawHtml, "<pre[^>]*>(.*?)</pre>", "\n```\n$1\n```\n", RegexOptions.IgnoreCase | RegexOptions.Singleline);

                    rawHtml = Regex.Replace(rawHtml, "<a[^>]+href=\"([^\"]+)\"[^>]*>(.*?)</a>", "[$2]($1)", RegexOptions.IgnoreCase | RegexOptions.Singleline);

                    var tempDoc = new HtmlDocument();
                    tempDoc.LoadHtml(rawHtml);
                    var plainText = tempDoc.DocumentNode.InnerText;

                    plainText = Regex.Replace(plainText, @"\n{3,}", "\n\n");

                    return plainText.Trim();
                }
            }

            return string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching release notes from Pub.dev for package {PackageId} version {Version}", packageId, version);
            return string.Empty;
        }
    }

    public async Task<string?> GetGitHubRepositoryUrlAsync(string packageId)
    {
        try
        {
            var url = $"{_baseUrl}/{PackageUrlEncoder.EscapeSegment(packageId)}";
            var html = await _httpClient.GetStringAsync(url);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var links = doc.DocumentNode.SelectNodes("//a[@href]");
            if (links != null)
            {
                foreach (var link in links)
                {
                    var href = link.GetAttributeValue("href", string.Empty);
                    if (GitHubUrls.IsGitHub(href) && link.InnerText.Contains("Repository"))
                    {
                        return href;
                    }
                }
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching GitHub repository URL from Pub.dev for package {PackageId}", packageId);
            return null;
        }
    }

    public async Task<(string? Publisher, string? Description, string? RepositoryUrl)> GetPackageExtraInfoAsync(string packageId)
    {
        string? publisher = null;
        string? description = null;
        string? repositoryUrl = null;

        try
        {
            var url = $"{_baseUrl}/{PackageUrlEncoder.EscapeSegment(packageId)}";
            var html = await _httpClient.GetStringAsync(url);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var metaNode = doc.DocumentNode.SelectSingleNode("//meta[@name='pub-page-data']");
            if (metaNode != null)
            {
                var base64Data = metaNode.GetAttributeValue("content", "");
                if (!string.IsNullOrEmpty(base64Data))
                {
                    var jsonStr = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(base64Data));
                    var match = Regex.Match(jsonStr, "\"publisherId\"\\s*:\\s*\"([^\"]+)\"");
                    if (match.Success)
                    {
                        publisher = match.Groups[1].Value;
                    }
                }
            }

            var descNode = doc.DocumentNode.SelectSingleNode("//meta[@property='og:description']");
            if (descNode != null)
            {
                description = descNode.GetAttributeValue("content", "");
            }

            var links = doc.DocumentNode.SelectNodes("//a[@href]");
            if (links != null)
            {
                foreach (var link in links)
                {
                    var href = link.GetAttributeValue("href", string.Empty);
                    if (GitHubUrls.IsGitHub(href) && link.InnerText.Contains("Repository"))
                    {
                        repositoryUrl = href;
                        break;
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching extra info from Pub.dev for package {PackageId}", packageId);
        }

        return (publisher, description, repositoryUrl);
    }

    public async Task<List<(string Version, DateTime? PublishDate)>> GetAllVersionsAsync(string packageId)
    {
        var versions = new List<(string, DateTime?)>();
        try
        {
            var url = $"{_baseUrl}/{PackageUrlEncoder.EscapeSegment(packageId)}/versions";
            var html = await _httpClient.GetStringAsync(url);
            var doc = new HtmlDocument();
            doc.LoadHtml(html);

            var rows = doc.DocumentNode.SelectNodes("//tr[@data-version]");
            if (rows != null)
            {
                foreach (var row in rows)
                {
                    var versionStr = row.GetAttributeValue("data-version", "");
                    if (string.IsNullOrEmpty(versionStr)) continue;

                    DateTime? publishDate = null;
                    var dateNode = row.SelectSingleNode(".//a[@class='-x-ago']");
                    if (dateNode != null)
                    {
                        var timestampStr = dateNode.GetAttributeValue("data-timestamp", "");
                        if (long.TryParse(timestampStr, out var timestampMs))
                        {
                            publishDate = DateTimeOffset.FromUnixTimeMilliseconds(timestampMs).UtcDateTime;
                        }
                    }

                    versions.Add((versionStr, publishDate));
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching all versions from Pub.dev for package {PackageId}", packageId);
        }

        return versions;
    }
}
