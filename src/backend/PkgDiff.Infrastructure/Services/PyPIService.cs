using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Versioning;
using PkgDiff.Shared.DTOs;
using PkgDiff.Shared.Validation;

namespace PkgDiff.Infrastructure.Services;

public class PyPIService : IPyPIService
{
    private const VersionScheme Scheme = VersionScheme.Pep440;

    private readonly HttpClient _httpClient;
    private readonly ILogger<PyPIService>? _logger;

    public PyPIService(IHttpClientFactory httpClientFactory, ILogger<PyPIService>? logger = null)
    {
        _httpClient = httpClientFactory.CreateClient(HttpClientNames.PyPIRegistry);
        _logger = logger;
    }

    public async Task<IEnumerable<string>> GetPackageVersionsAsync(string packageId, bool includePrerelease = false)
    {
        var packageData = await GetPackageDataAsync(packageId);
        if (packageData?.Releases == null)
        {
            return Enumerable.Empty<string>();
        }

        var versions = PackageVersions.ParseAll(Scheme, packageData.Releases.Keys, includePrerelease, out var skipped);
        if (skipped.Count > 0)
        {
            _logger?.LogWarning(
                "Skipped {Count} versions of {PackageId} that are not valid PEP 440: {Versions}",
                skipped.Count, packageId, string.Join(", ", skipped.Take(10)));
        }

        return versions.Select(v => v.Value).ToList();
    }

    public async Task<IEnumerable<string>> GetVersionsInRangeAsync(
        string packageId, 
        string fromVersion, 
        string toVersion, 
        bool includePrerelease = false)
    {
        var allVersions = await GetPackageVersionsAsync(packageId, includePrerelease);
        var fromParsed = TryParseVersion(fromVersion);
        var toParsed = TryParseVersion(toVersion);

        if (fromParsed == null || toParsed == null)
        {
            return Enumerable.Empty<string>();
        }

        return allVersions
            .Select(v => new { Version = v, Parsed = TryParseVersion(v) })
            .Where(x => x.Parsed != null && x.Parsed > fromParsed && x.Parsed <= toParsed)
            .OrderBy(x => x.Parsed)
            .Select(x => x.Version)
            .ToList();
    }

    public async Task<bool> PackageExistsAsync(string packageId)
    {
        try
        {
            var packageData = await GetPackageDataAsync(packageId);
            return packageData != null;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "PyPI lookup failed for {PackageId}", packageId);
            return false;
        }
    }

    public async Task<DateTime?> GetPackagePublishedDateAsync(string packageId, string version)
    {
        try
        {
            var packageData = await GetPackageDataAsync(packageId);
            if (packageData?.Releases != null && packageData.Releases.TryGetValue(version, out var releaseFiles) && releaseFiles.Any())
            {
                // Return the upload time of the first file for this version
                return releaseFiles.First().UploadTime;
            }
            return null;
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "PyPI lookup failed for {PackageId}", packageId);
            return null;
        }
    }

    public async Task<PackageMetadata> GetPackageMetadataAsync(string packageId)
    {
        var packageData = await GetPackageDataAsync(packageId);
        if (packageData == null)
        {
            throw new ArgumentException($"Package '{packageId}' not found on PyPI.");
        }

        return MapToPackageMetadata(packageId, packageData);
    }

    private async Task<PyPIPackageData?> GetPackageDataAsync(string packageId)
    {
        try
        {
            using var response = await _httpClient.GetAsync($"{PackageUrlEncoder.EscapeSegment(packageId)}/json");
            if (!response.IsSuccessStatusCode)
            {
                return null;
            }

            var content = await response.Content.ReadAsStringAsync();
            return JsonSerializer.Deserialize<PyPIPackageData>(content, new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
        }
        catch (Exception ex)
        {
            _logger?.LogWarning(ex, "PyPI lookup failed for {PackageId}", packageId);
            return null;
        }
    }

    private static bool IsPrerelease(string version) =>
        PackageVersion.TryParse(Scheme, version, out var parsed) && parsed.IsPrerelease;

    private static PackageVersion? TryParseVersion(string version) =>
        PackageVersion.TryParse(Scheme, version, out var parsed) ? parsed : null;

    private static bool IsGitHubUrl(string? url)
    {
        return GitHubUrls.IsGitHub(url);
    }

    // project_urls keys that name the repository itself, most telling first
    private static readonly string[] RepositoryKeys =
        { "Source", "Source Code", "Source code", "Code", "Repository", "GitHub", "Github", "Homepage", "Home" };

    // Links that point at GitHub but not at the repository: sponsorship pages above all
    private static readonly string[] NotRepositoryKeys = { "Funding", "Sponsor", "Sponsors", "Donate", "Donation" };

    private static readonly string[] NotRepositoryOwners = { "sponsors", "orgs", "users", "apps", "marketplace", "settings" };

    // The repository among the links of a package: the ones labelled as the source first, then the home
    // page, then any other GitHub link. "Funding" (github.com/sponsors/Kludex for uvicorn) never counts.
    private static string? ExtractGitHubUrl(PyPIPackageInfo? info)
    {
        if (info == null)
        {
            return null;
        }

        var links = info.ProjectUrls ?? new Dictionary<string, string>();
        var candidates = RepositoryKeys
            .Select(key => links.FirstOrDefault(l => l.Key.Equals(key, StringComparison.OrdinalIgnoreCase)).Value)
            .Append(info.HomePage)
            .Concat(links.Where(l => !NotRepositoryKeys.Any(k => l.Key.Contains(k, StringComparison.OrdinalIgnoreCase)))
                .Select(l => l.Value));

        return candidates.Select(RepositoryRoot).FirstOrDefault(url => url != null)
            ?? RepositoryFromDescription(info);
    }

    private static string? RepositoryFromDescription(PyPIPackageInfo info)
    {
        if (string.IsNullOrEmpty(info.Description) || string.IsNullOrEmpty(info.Name))
            return null;

        var name = NormalizeName(info.Name);
        return System.Text.RegularExpressions.Regex.Matches(info.Description,
                @"https?://(?:www\.)?github\.com/[\w.-]+/[\w.-]+",
                System.Text.RegularExpressions.RegexOptions.IgnoreCase)
            .Select(m => RepositoryRoot(m.Value.TrimEnd('.')))
            .FirstOrDefault(url => url != null && NormalizeName(url[(url.LastIndexOf('/') + 1)..]) == name);
    }

    private static string NormalizeName(string name) =>
        System.Text.RegularExpressions.Regex.Replace(name, @"[-_.]+", "-").ToLowerInvariant();

    // https://github.com/{owner}/{repo} of a GitHub link (issues, a changelog file, releases), keeping a
    // /tree/{ref}/{folder} part that names the package folder of a monorepo; null for any other link
    private static string? RepositoryRoot(string? url)
    {
        if (!IsGitHubUrl(url))
            return null;

        var match = System.Text.RegularExpressions.Regex.Match(url!,
            @"github\.com/(?<owner>[\w.-]+)/(?<repo>[\w.-]+)(?<tree>/tree/[^?#]+)?",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!match.Success || NotRepositoryOwners.Contains(match.Groups["owner"].Value, StringComparer.OrdinalIgnoreCase))
            return null;

        var repo = match.Groups["repo"].Value;
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            repo = repo[..^4];
        return $"https://github.com/{match.Groups["owner"].Value}/{repo}{match.Groups["tree"].Value.TrimEnd('/')}";
    }

    private PackageMetadata MapToPackageMetadata(string packageId, PyPIPackageData packageData)
    {
        var info = packageData.Info;
        string? githubUrl = ExtractGitHubUrl(info);

        var latestVersion = info?.Version ?? "unknown";

        return new PackageMetadata
        {
            PackageId = packageId,
            LatestVersion = latestVersion,
            Description = info?.Summary ?? "",
            Authors = info?.Author ?? "Unknown",
            IconUrl = null, 
            ProjectUrl = info?.HomePage
                ?? info?.ProjectUrls?.FirstOrDefault(l => l.Key.Equals("Homepage", StringComparison.OrdinalIgnoreCase)).Value
                ?? githubUrl,
            GitHubRepositoryUrl = githubUrl,
            Versions = packageData.Releases?
                .Where(v => !IsPrerelease(v.Key))
                .Select(v => new VersionSummary
                {
                    Version = v.Key,
                    PublishedDate = v.Value.FirstOrDefault()?.UploadTime,
                    IsPrerelease = false,
                })
                .OrderByDescending(v => TryParseVersion(v.Version))
                .ToList() ?? new List<VersionSummary>()
        };
    }

    // PyPI API response models
    private class PyPIPackageData
    {
        [JsonPropertyName("info")]
        public PyPIPackageInfo? Info { get; set; }

        [JsonPropertyName("releases")]
        public Dictionary<string, List<PyPIReleaseFile>>? Releases { get; set; }
    }

    private class PyPIPackageInfo
    {
        [JsonPropertyName("name")]
        public string? Name { get; set; }

        [JsonPropertyName("version")]
        public string? Version { get; set; }

        [JsonPropertyName("summary")]
        public string? Summary { get; set; }

        [JsonPropertyName("author")]
        public string? Author { get; set; }

        [JsonPropertyName("home_page")]
        public string? HomePage { get; set; }

        [JsonPropertyName("project_urls")]
        public Dictionary<string, string>? ProjectUrls { get; set; }

        [JsonPropertyName("description")]
        public string? Description { get; set; }
    }

    private class PyPIReleaseFile
    {
        [JsonPropertyName("upload_time")]
        public DateTime? UploadTime { get; set; }
    }
}
