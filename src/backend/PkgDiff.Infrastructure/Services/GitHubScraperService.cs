using System.Text.RegularExpressions;
using System.Text.Json;
using AngleSharp.Dom;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Infrastructure.Security;
using PkgDiff.Shared.Validation;
using Polly;
using Polly.Retry;

namespace PkgDiff.Infrastructure.Services;

public partial class GitHubScraperService : IGitHubScraperService
{
    private readonly AsyncRetryPolicy _retryPolicy;
    private readonly IRateLimiterService _rateLimiter;
    private readonly IGitHubRateLimitHandler _rateLimitHandler;
    private readonly ILogger<GitHubScraperService> _logger;
    private readonly IHtmlDocumentLoader _htmlDocumentLoader;
    private readonly HttpClient _httpClient;
    private readonly bool _enableAuthentication;
    private readonly bool _useGitHubApi;
    private readonly bool _sendsToken;
    private readonly GitHubScrapeCache _cache;
    private readonly GitHubChangelogReader _changelog;
    private readonly GitHubMilestoneReader _milestones;
    private readonly GitHubReleaseFileReader _releaseFiles;
    private readonly GitHubReleaseHtmlScraper _htmlScraper;

    public GitHubScraperService(
        IRateLimiterService rateLimiter, 
        IGitHubRateLimitHandler rateLimitHandler,
        Microsoft.Extensions.Configuration.IConfiguration configuration,
        IHttpClientFactory httpClientFactory,
        ILogger<GitHubScraperService> logger,
        IHtmlDocumentLoader htmlDocumentLoader,
        GitHubScrapeCache cache)
    {
        _rateLimiter = rateLimiter;
        _rateLimitHandler = rateLimitHandler;
        _logger = logger;
        _htmlDocumentLoader = htmlDocumentLoader;
        _cache = cache;

        var githubSettings = configuration.GetSection("GitHubSettings");
        var personalAccessToken = githubSettings.GetValue<string?>("PersonalAccessToken");
        _enableAuthentication = githubSettings.GetValue<bool>("EnableAuthentication", true);
        _useGitHubApi = githubSettings.GetValue<bool>("UseGitHubApi", true);
        var htmlScrapingTimeoutSeconds = githubSettings.GetValue<int>("HtmlScrapingTimeoutSeconds", 30);
        var userAgent = githubSettings.GetValue<string>("UserAgent", "PkgDiff/1.0");
        
        _httpClient = httpClientFactory.CreateClient(HttpClientNames.GitHubApi);
        _httpClient.DefaultRequestHeaders.Add("User-Agent", userAgent);
        _httpClient.DefaultRequestHeaders.Add("Accept", "application/vnd.github+json");
        
        _sendsToken = _enableAuthentication && !string.IsNullOrWhiteSpace(personalAccessToken);
        if (_sendsToken)
        {
            _httpClient.DefaultRequestHeaders.Add("Authorization", $"Bearer {personalAccessToken}");
        }

        _htmlScraper = new GitHubReleaseHtmlScraper(rateLimiter, htmlDocumentLoader, htmlScrapingTimeoutSeconds, logger);
        _changelog = new GitHubChangelogReader(_httpClient, rateLimiter, rateLimitHandler, cache, _useGitHubApi, logger);
        _releaseFiles = new GitHubReleaseFileReader(_httpClient, rateLimiter, rateLimitHandler, cache, _useGitHubApi && _enableAuthentication, logger);
        _milestones = new GitHubMilestoneReader(_httpClient, rateLimiter, rateLimitHandler, cache, _useGitHubApi && _enableAuthentication, logger);

        _retryPolicy = Policy
            .Handle<Exception>(TransientHttp.IsTransient)
            .WaitAndRetryAsync(3, retryAttempt =>
                TimeSpan.FromSeconds(Math.Pow(2, retryAttempt)));
    }

    public async Task<string?> ExtractGitHubRepositoryUrlAsync(string packageId)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            var url = $"https://www.nuget.org/packages/{PackageUrlEncoder.EscapeSegment(packageId)}";
            _logger.LogDebug("[GitHubScraper] Extracting GitHub repo URL for {PackageId} from {Url}", packageId, url);

            using var document = await _rateLimiter.ExecuteAsync("nuget-scraping", () => _htmlDocumentLoader.LoadAsync(url));
            if (document == null)
            {
                _logger.LogWarning("[GitHubScraper] No GitHub URL found for {PackageId}", packageId);
                return null;
            }

            var links = document.QuerySelectorAll("aside a[href*='github.com']");
            var githubLink = links.FirstOrDefault();

            if (githubLink != null)
            {
                var href = githubLink.GetAttribute("href");
                if (!string.IsNullOrWhiteSpace(href))
                {
                    var cleaned = GitHubUrls.Clean(href);
                    _logger.LogDebug("[GitHubScraper] Found GitHub URL for {PackageId}: {GitHubUrl}", packageId, cleaned);
                    return cleaned;
                }
            }

            _logger.LogWarning("[GitHubScraper] No GitHub URL found for {PackageId}", packageId);
            return null;
        });
    }

    public async Task<Dictionary<string, string>> GetAllReleasesAsync(string githubRepoUrl)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            var cleanUrl = GitHubUrls.Clean(githubRepoUrl);

            if (!await MayReadAsync(cleanUrl))
                return new Dictionary<string, string>();

            if (_cache.TryGetReleases(cleanUrl, out var cachedReleases))
            {
                _logger.LogDebug("[GitHubScraper] Using cached releases for {RepoUrl} ({Count} releases)",
                    cleanUrl, cachedReleases.Count);
                return cachedReleases;
            }

            Dictionary<string, string> releases;
            bool answered;
            if (_useGitHubApi && _enableAuthentication)
            {
                (releases, answered) = await GetAllReleasesViaApiAsync(githubRepoUrl);
            }
            else
            {
                releases = await _htmlScraper.GetAllReleasesAsync(githubRepoUrl);
                answered = releases.Count > 0;
            }

            // Cached even when empty: the caller asks again for every version
            _cache.SetReleases(cleanUrl, releases, answered);
            _logger.LogDebug("[GitHubScraper] Cached {Count} releases for {RepoUrl} (answered: {Answered})",
                releases.Count, cleanUrl, answered);

            return releases;
        });
    }

    // Answered is false when GitHub failed before the last page, so the list may be incomplete
    private async Task<(Dictionary<string, string> Releases, bool Answered)> GetAllReleasesViaApiAsync(string githubRepoUrl)
    {
        var releases = new Dictionary<string, string>();
        var (owner, repo) = GitHubUrls.Parse(githubRepoUrl, _logger);

        if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo))
        {
            _logger.LogWarning("[GitHubScraper] Invalid GitHub URL: {Url}", githubRepoUrl);
            return (releases, true);
        }

        int page = 1;
        int perPage = 100;
        bool hasMorePages = true;
        bool answered = false;

        _logger.LogDebug("[GitHubScraper] Fetching releases via GitHub API: {Owner}/{Repo}", owner, repo);

        while (hasMorePages)
        {
            var apiUrl = $"https://api.github.com/repos/{PackageUrlEncoder.EscapeSegment(owner)}/{PackageUrlEncoder.EscapeSegment(repo)}/releases?per_page={perPage}&page={page}";
            
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

                    // A missing repository will not have releases a minute later
                    answered = response.StatusCode == System.Net.HttpStatusCode.NotFound;
                    break;
                }

                var json = await response.Content.ReadAsStringAsync();
                using var releasesDocument = JsonDocument.Parse(json);
                var releasesArray = releasesDocument.RootElement;

                if (releasesArray.GetArrayLength() == 0)
                {
                    hasMorePages = false;
                    answered = true;
                    break;
                }

                foreach (var release in releasesArray.EnumerateArray())
                {
                    var tagName = release.GetProperty("tag_name").GetString();
                    var body = release.TryGetProperty("body", out var bodyProp) ? bodyProp.GetString() : null;
                    
                    if (!string.IsNullOrWhiteSpace(tagName) && !string.IsNullOrWhiteSpace(body))
                    {
                        var notes = GitHubReleasePage.CleanReleaseNotes(body);

                        releases.TryAdd(tagName, notes);
                        if (PlainVersionTag().IsMatch(tagName))
                            releases.TryAdd(tagName.TrimStart('v', 'V'), notes);
                        _logger.LogDebug("[GitHubScraper] Found release via API: {TagName} (notes length: {Length})", tagName, body.Length);
                    }
                }

                hasMorePages = releasesArray.GetArrayLength() == perPage;
                answered = !hasMorePages;
                page++;
                
                _logger.LogDebug("[GitHubScraper] Fetched page {Page}, releases so far: {Count}", page - 1, releases.Count);
            }
            catch (GitHubRateLimitedException)
            {
                // Nothing is cached: the list is read again once the limit resets
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[GitHubScraper] Failed to fetch releases page {Page} via API, stopping pagination", page);
                break;
            }
        }

        _logger.LogDebug("[GitHubScraper] Total releases extracted via API: {Count}. Versions: [{Versions}]",
            releases.Count, string.Join(", ", releases.Keys.Take(20)));

        return (releases, answered);
    }
    
    public Task<string?> GetReleaseNotesForVersionAsync(string githubRepoUrl, string version) =>
        GetReleaseNotesForVersionAsync(githubRepoUrl, version, null);

    public async Task<string?> GetReleaseNotesForVersionAsync(string githubRepoUrl, string version, string? packageId)
    {
        return await _retryPolicy.ExecuteAsync(async () =>
        {
            _logger.LogDebug("[GitHubScraper] Looking for release notes: repo={RepoUrl}, version={Version}", githubRepoUrl, version);

            if (!await MayReadAsync(githubRepoUrl))
                return null;

            string? notes = null;

            if (!_useGitHubApi || !_enableAuthentication)
            {
                notes = await _htmlScraper.GetReleaseNotesForVersionAsync(githubRepoUrl, version);
            }
            else
            {
                var allReleases = await GetAllReleasesAsync(GitHubUrls.Clean(githubRepoUrl));

                var cleanVersion = version.TrimStart('v', 'V');

                // Monorepos tag each package separately; the package name picks its tag
                var packageTag = TagCandidates(packageId, cleanVersion).FirstOrDefault(allReleases.ContainsKey);
                if (packageTag is not null)
                {
                    notes = allReleases[packageTag];
                    _logger.LogDebug("[GitHubScraper] Package tag {Tag} found for version {Version}", packageTag, cleanVersion);
                }
                else if (allReleases.TryGetValue(cleanVersion, out notes))
                {
                    _logger.LogDebug("[GitHubScraper] Exact match found for version {Version}", cleanVersion);
                }
                else if (allReleases.TryGetValue($"v{cleanVersion}", out notes))
                {
                    _logger.LogDebug("[GitHubScraper] Match found with 'v' prefix for version v{Version}", cleanVersion);
                }
                else if (ReleaseSuffix().Replace(cleanVersion, string.Empty) is var core && core != cleanVersion
                         && (allReleases.TryGetValue(core, out notes) || allReleases.TryGetValue($"v{core}", out notes)))
                {
                    _logger.LogDebug("[GitHubScraper] Match found for {Version} without its release suffix", cleanVersion);
                }
                else
                {
                    var fuzzyMatch = allReleases.FirstOrDefault(kvp =>
                        kvp.Key.Replace(".", "").Replace("-", "").Replace("_", "")
                            .Equals(cleanVersion.Replace(".", "").Replace("-", "").Replace("_", ""),
                                StringComparison.OrdinalIgnoreCase));
                    notes = fuzzyMatch.Value;

                    if (!string.IsNullOrWhiteSpace(notes))
                    {
                        _logger.LogDebug("[GitHubScraper] Fuzzy match found: {MatchedKey} for requested version {Version}", fuzzyMatch.Key, cleanVersion);
                    }
                    else
                    {
                        _logger.LogWarning("[GitHubScraper] NO MATCH found for version {Version} in {RepoUrl}. Available versions: [{AvailableVersions}]",
                            cleanVersion, githubRepoUrl, string.Join(", ", allReleases.Keys.Take(30)));
                    }
                }
            }

            if (string.IsNullOrWhiteSpace(notes) || GitHubChangelogReader.IsRedirection(notes))
            {
                _logger.LogDebug("[GitHubScraper] Notes are empty or redirection for version {Version}. Attempting to fetch from repository changelog files.", version);
                var changelogNotes = await _changelog.FetchAsync(githubRepoUrl, version, packageId);
                if (!string.IsNullOrWhiteSpace(changelogNotes))
                {
                    notes = changelogNotes;
                }
                else
                {
                    // No release and no changelog: try per-version release files, then milestones
                    var cleanUrl = GitHubUrls.Clean(githubRepoUrl);
                    var otherNotes = await _releaseFiles.FetchAsync(cleanUrl, version)
                                     ?? await _milestones.FetchAsync(cleanUrl, version);
                    if (!string.IsNullOrWhiteSpace(otherNotes))
                    {
                        notes = otherNotes;
                    }
                }
            }

            return notes;
        });
    }

    public async Task<bool?> IsPublicRepositoryAsync(string githubRepoUrl)
    {
        try
        {
            return await RepositoryVisibilityAsync(githubRepoUrl);
        }
        catch (GitHubRateLimitedException)
        {
            return null;
        }
    }

    // The token may see private repositories; their content must never be published
    private async Task<bool> MayReadAsync(string githubRepoUrl)
    {
        var isPublic = await RepositoryVisibilityAsync(githubRepoUrl);
        if (isPublic == true || (isPublic == null && !_sendsToken))
            return true;

        _logger.LogInformation("[GitHubScraper] Not reading {RepoUrl}: not a public repository ({Visibility})",
            githubRepoUrl, isPublic == false ? "private or missing" : "unchecked");
        return false;
    }

    private async Task<bool?> RepositoryVisibilityAsync(string githubRepoUrl)
    {
        var (owner, repo) = GitHubUrls.Parse(githubRepoUrl, _logger);
        if (string.IsNullOrEmpty(owner) || string.IsNullOrEmpty(repo))
            return false;

        // Without a token nothing private can be read
        if (!_sendsToken)
            return null;

        var repoKey = $"{owner}/{repo}".ToLowerInvariant();
        if (_cache.TryGetVisibility(repoKey, out var cached))
            return cached;

        var apiUrl = $"https://api.github.com/repos/{PackageUrlEncoder.EscapeSegment(owner)}/{PackageUrlEncoder.EscapeSegment(repo)}";
        try
        {
            await _rateLimitHandler.CanMakeRequestAsync();
            using var response = await _rateLimiter.ExecuteAsync("github-api", async () => await _httpClient.GetAsync(apiUrl));
            await _rateLimitHandler.UpdateFromHeadersAsync(response);
            GitHubRateLimitResponse.ThrowIfLimited(response);

            bool? isPublic = null;
            if (response.StatusCode == System.Net.HttpStatusCode.NotFound)
            {
                isPublic = false;
            }
            else if (response.IsSuccessStatusCode)
            {
                using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
                isPublic = document.RootElement.TryGetProperty("private", out var isPrivate)
                    && isPrivate.ValueKind == JsonValueKind.False;
            }

            if (isPublic.HasValue)
                _cache.SetVisibility(repoKey, isPublic.Value);
            else
                _logger.LogWarning("[GitHubScraper] API returned {StatusCode} for {Url}", response.StatusCode, apiUrl);
            return isPublic;
        }
        catch (GitHubRateLimitedException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "[GitHubScraper] Failed to read {Url}", apiUrl);
            return null;
        }
    }

    // Tag spellings of monorepos and package-named tags, most specific first
    private static IEnumerable<string> TagCandidates(string? packageId, string version)
    {
        if (string.IsNullOrWhiteSpace(packageId))
            yield break;

        var names = new List<string> { packageId };
        var separator = packageId.LastIndexOfAny(new[] { '/', ':' });
        if (separator >= 0 && separator < packageId.Length - 1)
            names.Add(packageId[(separator + 1)..]);

        foreach (var name in names)
        {
            yield return $"{name}@{version}";
            yield return $"{name}@v{version}";
            yield return $"{name}-v{version}";
            yield return $"{name}-{version}";
            yield return $"{name}/v{version}";
            yield return $"{name}/{version}";
            yield return $"{name}_v{version}";
            yield return $"{name}-parent-{version}";
        }
        yield return $"release-{version}";
        yield return $"release/{version}";
        yield return $"rel_{version.Replace('.', '_')}";
        yield return $"parent-{version}";
        yield return $"r{version}";
        yield return $"v_{version}";
    }

    [System.Text.RegularExpressions.GeneratedRegex(@"^[vV]?\d")]
    private static partial System.Text.RegularExpressions.Regex PlainVersionTag();

    // 6.4.0.Final and 33.0.0-jre are tagged 6.4.0 and v33.0.0
    [System.Text.RegularExpressions.GeneratedRegex(@"[.-](?:final|release|ga|jre|android)$", System.Text.RegularExpressions.RegexOptions.IgnoreCase)]
    private static partial System.Text.RegularExpressions.Regex ReleaseSuffix();
}
