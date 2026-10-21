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
            var packageUrl = $"{_baseUrl}/{PackageUrlEncoder.EscapeSegment(packageId)}";
            // The package's changelog page shows only the latest upload; each version's page keeps the file it shipped with
            return await ReadChangelogEntryAsync($"{packageUrl}/versions/{PackageUrlEncoder.EscapeSegment(version)}/changelog", version)
                ?? await ReadChangelogEntryAsync($"{packageUrl}/changelog", version)
                ?? string.Empty;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching release notes from Pub.dev for package {PackageId} version {Version}", packageId, version);
            return string.Empty;
        }
    }

    // Null when the page or its entry for the version is missing
    private async Task<string?> ReadChangelogEntryAsync(string url, string version)
    {
        using var response = await _httpClient.GetAsync(url);
        if (!response.IsSuccessStatusCode)
            return null;

        var doc = new HtmlDocument();
        doc.LoadHtml(await response.Content.ReadAsStringAsync());

        var changelogNodes = doc.DocumentNode.SelectNodes("//div[@class='changelog-entry']");
        if (changelogNodes == null)
            return null;

        foreach (var node in changelogNodes)
        {
            var headerNode = node.SelectSingleNode(".//h2");
            if (headerNode == null || !IsHeaderOf(headerNode.InnerText, version))
                continue;

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

        return null;
    }

    private static bool IsHeaderOf(string headerText, string version)
    {
        var firstWord = headerText.Split((char[]?)null, 2, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? string.Empty;
        return firstWord.Trim('[', ']', '(', ')', ':', ',').TrimStart('v', 'V')
            .Equals(version, StringComparison.OrdinalIgnoreCase);
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

            repositoryUrl ??= await GetPubspecGitHubUrlAsync(packageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error fetching extra info from Pub.dev for package {PackageId}", packageId);
        }

        return (publisher, description, repositoryUrl);
    }

    // The page links no repository when the pubspec names its GitHub repository only as the homepage
    private async Task<string?> GetPubspecGitHubUrlAsync(string packageId)
    {
        try
        {
            var json = await _httpClient.GetStringAsync($"https://pub.dev/api/packages/{PackageUrlEncoder.EscapeSegment(packageId)}");
            using var doc = JsonDocument.Parse(json);
            if (!doc.RootElement.TryGetProperty("latest", out var latest) || !latest.TryGetProperty("pubspec", out var pubspec))
                return null;

            foreach (var field in new[] { "repository", "homepage" })
            {
                if (pubspec.TryGetProperty(field, out var value) && value.ValueKind == JsonValueKind.String
                    && GitHubUrls.IsGitHub(value.GetString()))
                    return value.GetString();
            }
            return null;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException)
        {
            _logger.LogWarning(ex, "Could not read the pubspec of {PackageId} from the pub.dev API", packageId);
            return null;
        }
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
