using System.Collections.Concurrent;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Versioning;
using PkgDiff.Shared.Constants;
using PkgDiff.Shared.DTOs;
using PkgDiff.Shared.Validation;

namespace PkgDiff.Infrastructure.Services;

// Maven Central: versions from maven-metadata.xml, dates from the Central search API, the repository from the POM
public partial class MavenService : IMavenService
{
    private const VersionScheme Scheme = VersionScheme.Maven;
    private const int MaxParents = 4;
    private const int SearchPageSize = 200;
    private const int MaxSearchPages = 5;

    private static readonly string[] NotRepositoryOwners = { "sponsors", "orgs", "users", "apps", "marketplace", "settings" };

    private readonly HttpClient _repository;
    private readonly HttpClient _search;
    private readonly ILogger<MavenService> _logger;

    // An analysis asks for many versions of one artifact; each file is read once per scope
    private readonly ConcurrentDictionary<string, Task<List<PackageVersion>?>> _versions = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task<Dictionary<string, DateTime>>> _dates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Task<XElement?>> _poms = new(StringComparer.Ordinal);

    public MavenService(IHttpClientFactory httpClientFactory, ILogger<MavenService> logger)
    {
        _repository = httpClientFactory.CreateClient(HttpClientNames.MavenCentral);
        _search = httpClientFactory.CreateClient(HttpClientNames.MavenSearch);
        _logger = logger;
    }

    public async Task<IEnumerable<string>> GetPackageVersionsAsync(string packageId, bool includePrerelease = false)
    {
        var versions = await GetVersionsAsync(packageId);
        if (versions is null)
            return Enumerable.Empty<string>();

        return versions
            .Where(v => includePrerelease || !v.IsPrerelease)
            .Select(v => v.Value)
            .ToList();
    }

    public async Task<DateTime?> GetPackagePublishedDateAsync(string packageId, string version)
    {
        if (Coordinates(packageId) is not { } coordinates)
            return null;

        var dates = await GetDatesAsync(packageId);
        if (dates.TryGetValue(version, out var date))
            return date;

        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, PomPath(coordinates.Group, coordinates.Artifact, version));
            using var response = await _repository.SendAsync(request);
            return response.IsSuccessStatusCode ? response.Content.Headers.LastModified?.UtcDateTime : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Maven Central date lookup failed for {PackageId} {Version}", packageId, version);
            return null;
        }
    }

    public async Task<PackageMetadata> GetPackageMetadataAsync(string packageId)
    {
        var versions = await GetVersionsAsync(packageId);
        if (versions is null || versions.Count == 0 || Coordinates(packageId) is not { } coordinates)
            throw new ArgumentException($"Package '{packageId}' not found on Maven Central.");

        var stable = versions.Where(v => !v.IsPrerelease).ToList();
        var latest = stable.Count > 0 ? stable[^1] : versions[^1];
        var datesTask = GetDatesAsync(packageId);

        var pom = await GetPomAsync(coordinates.Group, coordinates.Artifact, latest.Value);
        var chain = pom is null ? new List<XElement>() : await ParentChainAsync(pom, coordinates.Group);
        var properties = PropertiesOf(chain, coordinates.Group, coordinates.Artifact, latest.Value);
        var repository = RepositoryOf(chain, coordinates.Group, properties);
        var dates = await datesTask;

        var homepage = Interpolate(Text(pom, "url"), properties);
        return new PackageMetadata
        {
            PackageId = packageId,
            LatestVersion = latest.Value,
            Description = Interpolate(Text(pom, "description"), properties)?.Trim() ?? Interpolate(Text(pom, "name"), properties) ?? string.Empty,
            Authors = AuthorsOf(chain, properties) ?? "Unknown",
            ProjectUrl = IsWebAddress(homepage) ? homepage : repository,
            GitHubRepositoryUrl = repository,
            Versions = stable
                .AsEnumerable()
                .Reverse()
                .Select(v => new VersionSummary
                {
                    Version = v.Value,
                    PublishedDate = dates.TryGetValue(v.Value, out var date) ? date : null,
                    IsPrerelease = false
                })
                .ToList()
        };
    }

    private Task<List<PackageVersion>?> GetVersionsAsync(string packageId) =>
        _versions.GetOrAdd(packageId, FetchVersionsAsync);

    // Ascending by Maven's ordering; 1.0 and 1.0.0 are one version, the first listed spelling kept
    private async Task<List<PackageVersion>?> FetchVersionsAsync(string packageId)
    {
        if (Coordinates(packageId) is not { } coordinates)
            return null;

        var metadata = await LoadXmlAsync($"{ArtifactPath(coordinates.Group, coordinates.Artifact)}/maven-metadata.xml", packageId);
        if (metadata is null)
            return null;

        var listed = metadata.Descendants()
            .Where(e => e.Name.LocalName == "version" && e.Parent?.Name.LocalName == "versions")
            .Select(e => e.Value.Trim())
            .Where(v => v.Length > 0)
            .ToList();

        var versions = PackageVersions.ParseAll(Scheme, listed, includePrerelease: true, out var skipped);
        if (skipped.Count > 0)
        {
            _logger.LogWarning(
                "Skipped {Count} versions of {PackageId} that Maven cannot order: {Versions}",
                skipped.Count, packageId, string.Join(", ", skipped.Take(10)));
        }

        return versions.Where((v, i) => i == 0 || !v.Equals(versions[i - 1])).ToList();
    }

    private Task<Dictionary<string, DateTime>> GetDatesAsync(string packageId) =>
        _dates.GetOrAdd(packageId, FetchDatesAsync);

    private async Task<Dictionary<string, DateTime>> FetchDatesAsync(string packageId)
    {
        var dates = new Dictionary<string, DateTime>(StringComparer.Ordinal);
        if (Coordinates(packageId) is not { } coordinates)
            return dates;

        var query = Uri.EscapeDataString($"g:\"{coordinates.Group}\" AND a:\"{coordinates.Artifact}\"");
        try
        {
            for (var page = 0; page < MaxSearchPages; page++)
            {
                var answer = await _search.GetFromJsonAsync<JsonElement>(
                    $"solrsearch/select?q={query}&core=gav&rows={SearchPageSize}&start={page * SearchPageSize}&wt=json");
                if (!answer.TryGetProperty("response", out var response)
                    || !response.TryGetProperty("docs", out var docs)
                    || docs.ValueKind != JsonValueKind.Array)
                    break;

                foreach (var doc in docs.EnumerateArray())
                {
                    if (doc.TryGetProperty("v", out var v) && v.ValueKind == JsonValueKind.String
                        && doc.TryGetProperty("timestamp", out var timestamp) && timestamp.TryGetInt64(out var milliseconds))
                        dates.TryAdd(v.GetString()!, DateTimeOffset.FromUnixTimeMilliseconds(milliseconds).UtcDateTime);
                }

                if (docs.GetArrayLength() < SearchPageSize)
                    break;
            }
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException or NotSupportedException)
        {
            // Dates are a nicety: the versions still list without them
            _logger.LogWarning(ex, "Maven Central search failed for {PackageId}", packageId);
        }
        return dates;
    }

    private Task<XElement?> GetPomAsync(string group, string artifact, string version) =>
        _poms.GetOrAdd($"{group}:{artifact}:{version}", _ => LoadXmlAsync(PomPath(group, artifact, version), $"{group}:{artifact}"));

    // The POM first, then each parent it inherits from
    private async Task<List<XElement>> ParentChainAsync(XElement pom, string group)
    {
        var chain = new List<XElement> { pom };
        var current = pom;
        for (var i = 0; i < MaxParents; i++)
        {
            var parent = Child(current, "parent");
            var parentGroup = Text(parent, "groupId");
            var parentArtifact = Text(parent, "artifactId");
            var parentVersion = Text(parent, "version");
            if (parentGroup is null || parentArtifact is null || parentVersion is null
                || !PackageIdRules.IsValidPackageId(RegistryNames.Maven, $"{parentGroup}:{parentArtifact}")
                || !PackageIdRules.IsValidVersion(parentVersion))
                break;

            var next = await GetPomAsync(parentGroup, parentArtifact, parentVersion);
            if (next is null)
                break;
            chain.Add(next);
            current = next;
        }
        return chain;
    }

    // Values are inherited, the child's winning; project.* always names the artifact itself
    private static Dictionary<string, string> PropertiesOf(List<XElement> chain, string group, string artifact, string version)
    {
        var properties = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var pom in chain.AsEnumerable().Reverse())
        {
            foreach (var property in Child(pom, "properties")?.Elements() ?? Enumerable.Empty<XElement>())
                properties[property.Name.LocalName] = property.Value.Trim();
        }

        foreach (var prefix in new[] { "project.", "pom.", "" })
        {
            properties[prefix + "groupId"] = group;
            properties[prefix + "artifactId"] = artifact;
            properties[prefix + "version"] = version;
        }
        if (chain.Count > 0 && Text(chain[0], "name") is { } name)
            properties["project.name"] = name;
        return properties;
    }

    // A parent's address counts only when it is of the same group or is filled in with this artifact's values
    private static string? RepositoryOf(List<XElement> chain, string group, Dictionary<string, string> properties)
    {
        for (var i = 0; i < chain.Count; i++)
        {
            var pom = chain[i];
            var inherited = i > 0;
            var sameGroup = (Text(pom, "groupId") ?? Text(Child(pom, "parent"), "groupId")) == group;
            var scm = Child(pom, "scm");
            var candidates = new[]
            {
                Text(scm, "url"), Text(scm, "connection"), Text(scm, "developerConnection"),
                Text(pom, "url"), Text(Child(pom, "issueManagement"), "url")
            };

            foreach (var candidate in candidates)
            {
                if (candidate is null || (inherited && !sameGroup && !candidate.Contains("${")))
                    continue;
                if (GitHubRepository(Interpolate(candidate, properties)) is { } repository)
                    return repository;
            }
        }
        return null;
    }

    private static string? AuthorsOf(List<XElement> chain, Dictionary<string, string> properties)
    {
        foreach (var pom in chain)
        {
            var names = Child(pom, "developers")?.Elements()
                .Select(d => Text(d, "name") ?? Text(d, "id"))
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Take(5)
                .ToList();
            if (names is { Count: > 0 })
                return Interpolate(string.Join(", ", names), properties);

            if (Interpolate(Text(Child(pom, "organization"), "name"), properties) is { Length: > 0 } organization)
                return organization;
        }
        return null;
    }

    private static string? GitHubRepository(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;

        var match = GitHubAddress().Match(text);
        if (!match.Success || NotRepositoryOwners.Contains(match.Groups["owner"].Value, StringComparer.OrdinalIgnoreCase))
            return null;

        var repo = match.Groups["repo"].Value;
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            repo = repo[..^4];
        return repo.Length == 0 ? null : $"https://github.com/{match.Groups["owner"].Value}/{repo}";
    }

    private static string? Interpolate(string? text, Dictionary<string, string> properties)
    {
        if (text is null)
            return null;

        for (var pass = 0; pass < 3 && text.Contains("${"); pass++)
            text = Placeholder().Replace(text, m => properties.TryGetValue(m.Groups["name"].Value, out var value) ? value : m.Value);
        return text.Trim();
    }

    private static bool IsWebAddress(string? url) =>
        Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp);

    private async Task<XElement?> LoadXmlAsync(string path, string packageId)
    {
        try
        {
            using var response = await _repository.GetAsync(path);
            if (!response.IsSuccessStatusCode)
                return null;

            await using var stream = await response.Content.ReadAsStreamAsync();
            // No DTD: a POM needs none, and one could expand entities
            using var reader = XmlReader.Create(stream, new XmlReaderSettings
            {
                DtdProcessing = DtdProcessing.Prohibit,
                XmlResolver = null,
                Async = true
            });
            return (await XDocument.LoadAsync(reader, LoadOptions.None, CancellationToken.None)).Root;
        }
        catch (Exception ex) when (ex is HttpRequestException or XmlException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Maven Central lookup failed for {PackageId}", packageId);
            return null;
        }
    }

    private static (string Group, string Artifact)? Coordinates(string packageId)
    {
        if (!PackageIdRules.IsValidPackageId(RegistryNames.Maven, packageId))
            return null;
        var colon = packageId.IndexOf(':');
        return (packageId[..colon], packageId[(colon + 1)..]);
    }

    private static string ArtifactPath(string group, string artifact) =>
        string.Join('/', group.Split('.').Select(PackageUrlEncoder.EscapeSegment)) + "/" + PackageUrlEncoder.EscapeSegment(artifact);

    private static string PomPath(string group, string artifact, string version)
    {
        var escapedVersion = PackageUrlEncoder.EscapeSegment(version);
        return $"{ArtifactPath(group, artifact)}/{escapedVersion}/{PackageUrlEncoder.EscapeSegment(artifact)}-{escapedVersion}.pom";
    }

    private static XElement? Child(XElement? element, string name) =>
        element?.Elements().FirstOrDefault(e => e.Name.LocalName == name);

    private static string? Text(XElement? element, string name) =>
        Child(element, name)?.Value.Trim() is { Length: > 0 } value ? value : null;

    [GeneratedRegex(@"github\.com[/:](?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubAddress();

    [GeneratedRegex(@"\$\{(?<name>[A-Za-z0-9_.-]+)\}")]
    private static partial Regex Placeholder();
}
