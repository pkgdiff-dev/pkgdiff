using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Services;
using PkgDiff.Shared.ReleaseNotes;
using PkgDiff.Shared.Validation;

namespace PkgDiff.Infrastructure.Services;

// Release notes from closed GitHub milestones named after versions, for projects with neither
// releases nor a changelog
public class GitHubMilestoneReader
{
    private const int PerPage = 100;

    // Sections in the order they are listed; an item goes to the first section one of its labels names
    private static readonly (string Title, string[] Labels)[] Sections =
    {
        ("Breaking changes", new[] { "breaking changes", "breaking change", "breaking" }),
        ("Security", new[] { "security" }),
        ("Features", new[] { "feature", "enhancement", "new feature" }),
        ("Bug fixes", new[] { "bug", "bug fix", "fix" }),
    };

    private readonly HttpClient _httpClient;
    private readonly IRateLimiterService _rateLimiter;
    private readonly IGitHubRateLimitHandler _rateLimitHandler;
    private readonly GitHubScrapeCache _cache;
    private readonly bool _useGitHubApi;
    private readonly ILogger _logger;

    public GitHubMilestoneReader(
        HttpClient httpClient,
        IRateLimiterService rateLimiter,
        IGitHubRateLimitHandler rateLimitHandler,
        GitHubScrapeCache cache,
        bool useGitHubApi,
        ILogger logger)
    {
        _httpClient = httpClient;
        _rateLimiter = rateLimiter;
        _rateLimitHandler = rateLimitHandler;
        _cache = cache;
        _useGitHubApi = useGitHubApi;
        _logger = logger;
    }

    public async Task<string?> FetchAsync(string githubRepoUrl, string version)
    {
        if (!_useGitHubApi)
            return null;

        var (owner, repo) = GitHubUrls.Parse(githubRepoUrl, _logger);
        if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo))
            return null;

        var milestones = await MilestonesAsync(owner, repo);
        if (!milestones.TryGetValue(version.TrimStart('v', 'V'), out var number))
            return null;

        var apiUrl = $"{RepoApi(owner, repo)}/issues?milestone={number}&state=closed&per_page={PerPage}";
        using var document = await GetJsonAsync(apiUrl);
        if (document is null || document.RootElement.ValueKind != JsonValueKind.Array)
            return null;

        var notes = Format(document.RootElement);
        if (notes is null)
            return null;

        _logger.LogDebug("[GitHubScraper] Release notes for {Version} built from milestone {Number} of {Owner}/{Repo}", version, number, owner, repo);

        // The maintainers did not write these notes: the reader is told where they come from
        var milestoneUrl = $"https://github.com/{PackageUrlEncoder.EscapeSegment(owner)}/{PackageUrlEncoder.EscapeSegment(repo)}/milestone/{number}?closed=1";
        return CompiledReleaseNotes.WithNotice(notes, $"{version.TrimStart('v', 'V')} milestone", milestoneUrl);
    }

    // Closed milestones by version, with or without a "v"; an empty map for a repository without any
    private async Task<Dictionary<string, int>> MilestonesAsync(string owner, string repo)
    {
        var repoKey = $"{owner}/{repo}".ToLowerInvariant();
        if (_cache.TryGetMilestones(repoKey, out var cached))
            return cached;

        var milestones = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var page = 1; ; page++)
        {
            using var document = await GetJsonAsync($"{RepoApi(owner, repo)}/milestones?state=closed&per_page={PerPage}&page={page}");
            // A failed page leaves the map unknown: retried soon, but not for every version
            if (document is null || document.RootElement.ValueKind != JsonValueKind.Array)
            {
                _cache.SetMilestones(repoKey, milestones, answered: false);
                return milestones;
            }

            foreach (var milestone in document.RootElement.EnumerateArray())
            {
                var title = milestone.TryGetProperty("title", out var titleProp) ? titleProp.GetString()?.Trim() : null;
                if (!string.IsNullOrEmpty(title) && milestone.TryGetProperty("number", out var numberProp))
                    milestones.TryAdd(title.TrimStart('v', 'V'), numberProp.GetInt32());
            }

            if (document.RootElement.GetArrayLength() < PerPage)
                break;
        }

        _cache.SetMilestones(repoKey, milestones);
        _logger.LogDebug("[GitHubScraper] {Count} closed milestones in {RepoKey}", milestones.Count, repoKey);
        return milestones;
    }

    private static string? Format(JsonElement issues)
    {
        var sections = Sections.Select(s => (s.Title, Items: new List<string>())).ToList();
        var other = new List<string>();

        foreach (var issue in issues.EnumerateArray())
        {
            var title = issue.TryGetProperty("title", out var titleProp) ? titleProp.GetString()?.Trim() : null;
            if (string.IsNullOrEmpty(title))
                continue;

            var number = issue.TryGetProperty("number", out var numberProp) ? numberProp.GetInt32() : 0;
            var url = issue.TryGetProperty("html_url", out var urlProp) ? urlProp.GetString() : null;
            var item = string.IsNullOrEmpty(url) ? $"- {title}" : $"- {title} ([#{number}]({url}))";

            var labels = issue.TryGetProperty("labels", out var labelsProp) && labelsProp.ValueKind == JsonValueKind.Array
                ? labelsProp.EnumerateArray()
                    .Select(l => l.TryGetProperty("name", out var name) ? name.GetString() ?? string.Empty : string.Empty)
                    .ToHashSet(StringComparer.OrdinalIgnoreCase)
                : new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            var index = Array.FindIndex(Sections, s => s.Labels.Any(labels.Contains));
            (index >= 0 ? sections[index].Items : other).Add(item);
        }

        sections.Add(("Other changes", other));
        var nonEmpty = sections.Where(s => s.Items.Count > 0).ToList();
        if (nonEmpty.Count == 0)
            return null;

        var builder = new StringBuilder();
        foreach (var (title, items) in nonEmpty)
        {
            if (builder.Length > 0)
                builder.Append('\n');
            builder.Append("### ").Append(title).Append('\n');
            foreach (var item in items)
                builder.Append(item).Append('\n');
        }
        return builder.ToString().TrimEnd();
    }

    private async Task<JsonDocument?> GetJsonAsync(string apiUrl)
    {
        try
        {
            await _rateLimitHandler.CanMakeRequestAsync();

            using var response = await _rateLimiter.ExecuteAsync("github-api", async () =>
            {
                _logger.LogDebug("[GitHubScraper] API Request: GET {ApiUrl}", apiUrl);
                return await _httpClient.GetAsync(apiUrl);
            });

            await _rateLimitHandler.UpdateFromHeadersAsync(response);

            if (!response.IsSuccessStatusCode)
            {
                GitHubRateLimitResponse.ThrowIfLimited(response);
                _logger.LogWarning("[GitHubScraper] API returned {StatusCode} for {Url}", response.StatusCode, apiUrl);
                return null;
            }

            return JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        }
        catch (OperationCanceledException) when (WorkCancellation.Token.IsCancellationRequested)
        {
            throw;
        }
        catch (GitHubRateLimitedException)
        {
            // Not "no milestones": the version is looked up again once the limit resets
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[GitHubScraper] Failed to read {Url}", apiUrl);
            return null;
        }
    }

    private static string RepoApi(string owner, string repo) =>
        $"https://api.github.com/repos/{PackageUrlEncoder.EscapeSegment(owner)}/{PackageUrlEncoder.EscapeSegment(repo)}";
}
