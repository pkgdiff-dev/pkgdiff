using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Shared.Constants;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Infrastructure.Services;

// Project health: deps.dev for npm, PyPI and NuGet (licenses, provenance, OpenSSF Scorecard of the
// source repository); the Scorecard API for a GitHub repository deps.dev does not cover, which is
// how pub.dev packages get theirs. deps.dev data is CC BY 4.0, so its attribution travels with it.
public partial class PackageHealthClient : IPackageHealthClient
{
    private const string DepsDevAttribution = "Data from deps.dev, licensed under CC BY 4.0";
    private const string ScorecardAttribution = "Data from the OpenSSF Scorecard API";

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly HttpClient _depsDev;
    private readonly HttpClient _scorecard;
    private readonly ILogger<PackageHealthClient> _logger;

    public PackageHealthClient(IHttpClientFactory httpClientFactory, ILogger<PackageHealthClient> logger)
    {
        _depsDev = httpClientFactory.CreateClient(HttpClientNames.DepsDev);
        _scorecard = httpClientFactory.CreateClient(HttpClientNames.Scorecard);
        _logger = logger;
    }

    public async Task<PackageHealth?> GetHealthAsync(
        string registry,
        string packageId,
        string? version,
        string? repositoryUrl,
        CancellationToken cancellationToken = default)
    {
        var system = SystemOf(registry);
        if (system is not null && !string.IsNullOrEmpty(version))
        {
            var health = await FromDepsDevAsync(system, NameFor(registry, packageId), version, repositoryUrl, cancellationToken);
            if (health is not null)
                return health;
        }

        var repository = GitHubProject(repositoryUrl);
        return repository is null ? null : await FromScorecardAsync(repository, cancellationToken);
    }

    private async Task<PackageHealth?> FromDepsDevAsync(string system, string name, string version, string? repositoryUrl, CancellationToken cancellationToken)
    {
        var versionPath = $"v3/systems/{system}/packages/{Uri.EscapeDataString(name)}/versions/{Uri.EscapeDataString(version)}";
        using var response = await _depsDev.GetAsync(versionPath, cancellationToken);
        if (response.StatusCode == HttpStatusCode.NotFound)
            return null;
        response.EnsureSuccessStatusCode();
        var packageVersion = await response.Content.ReadFromJsonAsync<JsonElement>(Json, cancellationToken);

        var health = new PackageHealth
        {
            Source = "deps.dev",
            SourceUrl = $"https://deps.dev/{system.ToLowerInvariant()}/{Uri.EscapeDataString(name)}/{Uri.EscapeDataString(version)}",
            Attribution = DepsDevAttribution,
            CheckedVersion = version,
            Licenses = Strings(packageVersion, "licenses"),
            HasProvenance = HasAny(packageVersion, "slsaProvenances") || HasAny(packageVersion, "attestations")
        };

        var project = SourceProject(packageVersion) ?? GitHubProject(repositoryUrl);
        if (project is null)
            return health;

        health.RepositoryUrl = "https://" + project;
        using var projectResponse = await _depsDev.GetAsync($"v3/projects/{Uri.EscapeDataString(project)}", cancellationToken);
        if (!projectResponse.IsSuccessStatusCode)
            return health;

        var projectData = await projectResponse.Content.ReadFromJsonAsync<JsonElement>(Json, cancellationToken);
        if (projectData.TryGetProperty("scorecard", out var scorecard) && scorecard.ValueKind == JsonValueKind.Object)
            ReadScorecard(scorecard, health);
        return health;
    }

    private async Task<PackageHealth?> FromScorecardAsync(string project, CancellationToken cancellationToken)
    {
        try
        {
            using var response = await _scorecard.GetAsync($"projects/{project}", cancellationToken);
            if (response.StatusCode == HttpStatusCode.NotFound)
                return null;
            response.EnsureSuccessStatusCode();
            var data = await response.Content.ReadFromJsonAsync<JsonElement>(Json, cancellationToken);

            var health = new PackageHealth
            {
                Source = "OpenSSF Scorecard",
                SourceUrl = $"https://scorecard.dev/viewer/?uri={Uri.EscapeDataString(project)}",
                Attribution = ScorecardAttribution,
                RepositoryUrl = "https://" + project
            };
            ReadScorecard(data, health);
            return health;
        }
        catch (Exception ex) when (ex is HttpRequestException or JsonException or TaskCanceledException)
        {
            _logger.LogWarning(ex, "Scorecard of {Project} could not be read", project);
            return null;
        }
    }

    // deps.dev and the Scorecard API use the same shape: overallScore/score, date, checks[]
    private static void ReadScorecard(JsonElement scorecard, PackageHealth health)
    {
        if (scorecard.TryGetProperty("overallScore", out var overall) && overall.ValueKind == JsonValueKind.Number)
            health.ScorecardScore = Math.Round(overall.GetDouble(), 1);
        else if (scorecard.TryGetProperty("score", out var score) && score.ValueKind == JsonValueKind.Number)
            health.ScorecardScore = Math.Round(score.GetDouble(), 1);

        if (scorecard.TryGetProperty("date", out var date) && date.ValueKind == JsonValueKind.String && date.TryGetDateTime(out var parsed))
            health.ScorecardDate = parsed.ToUniversalTime();

        if (!scorecard.TryGetProperty("checks", out var checks) || checks.ValueKind != JsonValueKind.Array)
            return;

        foreach (var check in checks.EnumerateArray())
        {
            var name = check.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : null;
            if (string.IsNullOrEmpty(name))
                continue;
            int? value = check.TryGetProperty("score", out var s) && s.ValueKind == JsonValueKind.Number ? s.GetInt32() : null;
            var reason = check.TryGetProperty("reason", out var r) && r.ValueKind == JsonValueKind.String ? r.GetString() : null;
            // -1 means the check did not apply to the repository
            health.Checks.Add(new ScorecardCheck { Name = name, Score = value is < 0 ? null : value, Reason = reason });
        }
        health.Checks = health.Checks.OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
    }

    private static string? SourceProject(JsonElement packageVersion)
    {
        if (!packageVersion.TryGetProperty("relatedProjects", out var related) || related.ValueKind != JsonValueKind.Array)
            return null;
        foreach (var project in related.EnumerateArray())
        {
            if (project.TryGetProperty("relationType", out var type) && type.GetString() == "SOURCE_REPO"
                && project.TryGetProperty("projectKey", out var key) && key.TryGetProperty("id", out var id)
                && id.ValueKind == JsonValueKind.String)
            {
                return id.GetString();
            }
        }
        return null;
    }

    private static List<string> Strings(JsonElement element, string property) =>
        element.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array
            ? array.EnumerateArray().Where(e => e.ValueKind == JsonValueKind.String).Select(e => e.GetString()!).Distinct().ToList()
            : new List<string>();

    private static bool HasAny(JsonElement element, string property) =>
        element.TryGetProperty(property, out var array) && array.ValueKind == JsonValueKind.Array && array.GetArrayLength() > 0;

    // "github.com/owner/repo" from any GitHub URL, or null
    private static string? GitHubProject(string? url)
    {
        if (string.IsNullOrWhiteSpace(url))
            return null;
        var match = GitHubRepository().Match(url);
        if (!match.Success)
            return null;
        var repo = match.Groups["repo"].Value;
        if (repo.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
            repo = repo[..^4];
        return $"github.com/{match.Groups["owner"].Value}/{repo}".ToLowerInvariant();
    }

    private static string? SystemOf(string registry) => RegistryNames.Canonical(registry) switch
    {
        RegistryNames.Npm => "NPM",
        RegistryNames.PyPI => "PYPI",
        RegistryNames.NuGet => "NUGET",
        _ => null
    };

    // deps.dev keys NuGet and PyPI packages by their normalized, lower-case names
    private static string NameFor(string registry, string packageId) => RegistryNames.Canonical(registry) switch
    {
        RegistryNames.PyPI => PyPINormalization().Replace(packageId.ToLowerInvariant(), "-"),
        RegistryNames.NuGet => packageId.ToLowerInvariant(),
        _ => packageId
    };

    [GeneratedRegex(@"github\.com[/:](?<owner>[A-Za-z0-9_.-]+)/(?<repo>[A-Za-z0-9_.-]+)", RegexOptions.IgnoreCase)]
    private static partial Regex GitHubRepository();

    [GeneratedRegex(@"[-_.]+")]
    private static partial Regex PyPINormalization();
}
