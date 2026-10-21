using System.Collections.Concurrent;
using System.Globalization;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Versioning;
using PkgDiff.Shared.DTOs;
using PkgDiff.Shared.Validation;

namespace PkgDiff.Infrastructure.Services;

// Tagged releases from the Composer v2 metadata (repo.packagist.org/p2), versions without their "v"
public class PackagistService : IPackagistService
{
    private const VersionScheme Scheme = VersionScheme.Composer;
    private const string Unset = "__unset";

    private readonly HttpClient _httpClient;
    private readonly ILogger<PackagistService> _logger;

    // An analysis asks for many versions of one package; its metadata is read once per scope
    private readonly ConcurrentDictionary<string, Task<List<PackagistRelease>?>> _releases = new(StringComparer.Ordinal);

    public PackagistService(IHttpClientFactory httpClientFactory, ILogger<PackagistService> logger)
    {
        _httpClient = httpClientFactory.CreateClient(HttpClientNames.PackagistRepository);
        _logger = logger;
    }

    public async Task<IEnumerable<string>> GetPackageVersionsAsync(string packageId, bool includePrerelease = false)
    {
        var releases = await GetReleasesAsync(packageId);
        if (releases is null)
            return Enumerable.Empty<string>();

        return releases
            .Where(r => includePrerelease || !r.Version.IsPrerelease)
            .Select(r => r.Version.Value)
            .ToList();
    }

    public async Task<DateTime?> GetPackagePublishedDateAsync(string packageId, string version)
    {
        var releases = await GetReleasesAsync(packageId);
        if (releases is null || !PackageVersion.TryParse(Scheme, StripV(version), out var wanted))
            return null;

        return releases.FirstOrDefault(r => r.Version.Equals(wanted))?.PublishedDate;
    }

    public async Task<PackageMetadata> GetPackageMetadataAsync(string packageId)
    {
        var releases = await GetReleasesAsync(packageId);
        if (releases is null || releases.Count == 0)
            throw new ArgumentException($"Package '{packageId}' not found on Packagist.");

        var stable = releases.Where(r => !r.Version.IsPrerelease).ToList();
        var latest = stable.Count > 0 ? stable[^1] : releases[^1];
        var repository = releases.AsEnumerable().Reverse().Select(r => r.RepositoryUrl).FirstOrDefault(url => url is not null);

        return new PackageMetadata
        {
            PackageId = NameOf(packageId),
            LatestVersion = latest.Version.Value,
            Description = latest.Description ?? string.Empty,
            Authors = latest.Authors ?? "Unknown",
            ProjectUrl = latest.Homepage ?? repository,
            GitHubRepositoryUrl = repository,
            Versions = stable
                .AsEnumerable()
                .Reverse()
                .Select(r => new VersionSummary
                {
                    Version = r.Version.Value,
                    PublishedDate = r.PublishedDate,
                    IsPrerelease = false
                })
                .ToList()
        };
    }

    private Task<List<PackagistRelease>?> GetReleasesAsync(string packageId) =>
        _releases.GetOrAdd(NameOf(packageId), FetchReleasesAsync);

    // Ascending by Composer's ordering
    private async Task<List<PackagistRelease>?> FetchReleasesAsync(string name)
    {
        if (name.Count(c => c == '/') != 1)
            return null;

        try
        {
            var path = string.Join('/', name.Split('/').Select(PackageUrlEncoder.EscapeSegment));
            using var response = await _httpClient.GetAsync($"p2/{path}.json");
            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync();
            using var document = await JsonDocument.ParseAsync(stream);
            if (!document.RootElement.TryGetProperty("packages", out var packages)
                || packages.ValueKind != JsonValueKind.Object
                || !packages.TryGetProperty(name, out var entries)
                || entries.ValueKind != JsonValueKind.Array)
                return null;

            var releases = new List<PackagistRelease>();
            var skipped = new List<string>();
            foreach (var fields in Expand(entries))
            {
                var spelled = Text(fields, "version");
                if (spelled is null)
                    continue;
                if (!PackageVersion.TryParse(Scheme, StripV(spelled), out var version))
                {
                    skipped.Add(spelled);
                    continue;
                }
                releases.Add(ReleaseOf(version, fields));
            }

            if (skipped.Count > 0)
            {
                _logger.LogWarning(
                    "Skipped {Count} versions of {PackageId} that Composer cannot order: {Versions}",
                    skipped.Count, name, string.Join(", ", skipped.Take(10)));
            }

            // v1.0 and 1.0 can both be tagged; the first listed, the newest metadata, is kept
            var ordered = releases.OrderBy(r => r.Version).ToList();
            return ordered.Where((r, i) => i == 0 || !r.Version.Equals(ordered[i - 1].Version)).ToList();
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Packagist lookup failed for {PackageId}", name);
            return null;
        }
    }

    // Each entry after the first lists only the fields that changed; "__unset" removes one
    private static IEnumerable<Dictionary<string, JsonElement>> Expand(JsonElement entries)
    {
        var current = new Dictionary<string, JsonElement>(StringComparer.Ordinal);
        foreach (var entry in entries.EnumerateArray())
        {
            if (entry.ValueKind != JsonValueKind.Object)
                continue;

            foreach (var field in entry.EnumerateObject())
            {
                if (field.Value.ValueKind == JsonValueKind.String && field.Value.ValueEquals(Unset))
                    current.Remove(field.Name);
                else
                    current[field.Name] = field.Value;
            }
            yield return current;
        }
    }

    private static PackagistRelease ReleaseOf(PackageVersion version, Dictionary<string, JsonElement> fields)
    {
        DateTime? published = DateTimeOffset.TryParse(Text(fields, "time"), CultureInfo.InvariantCulture,
            DateTimeStyles.AssumeUniversal, out var time)
            ? time.UtcDateTime
            : null;

        var authors = fields.TryGetValue("authors", out var list) && list.ValueKind == JsonValueKind.Array
            ? list.EnumerateArray()
                .Select(a => a.ValueKind == JsonValueKind.Object && a.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null)
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .ToList()
            : new List<string?>();

        return new PackagistRelease(
            version,
            published,
            Text(fields, "description"),
            Text(fields, "homepage"),
            authors.Count > 0 ? string.Join(", ", authors) : null,
            RepositoryOf(fields));
    }

    private static string? RepositoryOf(Dictionary<string, JsonElement> fields)
    {
        var candidates = new[] { Nested(fields, "source", "url"), Nested(fields, "support", "source"), Text(fields, "homepage") };
        return candidates
            .Where(GitHubUrls.IsGitHub)
            .Select(url => GitHubUrls.Clean(GitHubUrls.FromRepositoryField(url)!))
            .FirstOrDefault(url => url.Length > 0);
    }

    private static string? Text(Dictionary<string, JsonElement> fields, string name) =>
        fields.TryGetValue(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string? Nested(Dictionary<string, JsonElement> fields, string name, string property) =>
        fields.TryGetValue(name, out var value)
        && value.ValueKind == JsonValueKind.Object
        && value.TryGetProperty(property, out var inner)
        && inner.ValueKind == JsonValueKind.String
            ? inner.GetString()
            : null;

    private static string NameOf(string packageId) => packageId.Trim().ToLowerInvariant();

    private static string StripV(string version)
    {
        version = version.Trim();
        return version.Length > 1 && (version[0] == 'v' || version[0] == 'V') && char.IsDigit(version[1]) ? version[1..] : version;
    }

    private sealed record PackagistRelease(
        PackageVersion Version,
        DateTime? PublishedDate,
        string? Description,
        string? Homepage,
        string? Authors,
        string? RepositoryUrl);
}
