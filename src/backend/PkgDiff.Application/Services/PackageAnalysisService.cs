using System.Text.Json;
using Microsoft.Extensions.Logging;
using NuGet.Versioning;
using PkgDiff.Application.Diagnostics;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Versioning;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Shared.DTOs;
using PkgDiff.Shared.ReleaseNotes;
using Microsoft.Extensions.Caching.Distributed;

namespace PkgDiff.Application.Services;

public class PackageAnalysisService : IPackageAnalysisService
{
    private static readonly TimeSpan EmptyNotesRetryAfter = TimeSpan.FromDays(1);

    private const int MaxScrapedVersions = 300;

    private const int MaxListedPackages = 2000;

    // GitHub's reset time is in whole seconds and our clock may run ahead of it
    private static readonly TimeSpan RateLimitResetMargin = TimeSpan.FromMinutes(1);

    private readonly IRegistryServiceFactory _registryFactory;
    private readonly IPackageCacheService _cacheService;
    private readonly IAnalyzerService _analyzerService;
    private readonly IUnitOfWork _unitOfWork;
    private readonly IProgressReporter _progressReporter;
    private readonly IWorkTrackerService _workTracker;
    private readonly IContributorService _contributorService;
    private readonly IDistributedCache _distributedCache;
    private readonly IReleaseNotesRenderer _releaseNotesRenderer;
    private readonly ILiveUpdateService _liveUpdates;
    private readonly ICachedLookups _lookups;
    private readonly IJobScheduler _jobs;
    private readonly ILogger<PackageAnalysisService> _logger;

    public PackageAnalysisService(
        IRegistryServiceFactory registryFactory,
        IPackageCacheService cacheService,
        IAnalyzerService analyzerService,
        IUnitOfWork unitOfWork,
        IProgressReporter progressReporter,
        IWorkTrackerService workTracker,
        IContributorService contributorService,
        IDistributedCache distributedCache,
        IReleaseNotesRenderer releaseNotesRenderer,
        ILiveUpdateService liveUpdates,
        ICachedLookups lookups,
        IJobScheduler jobs,
        ILogger<PackageAnalysisService> logger)
    {
        _registryFactory = registryFactory;
        _cacheService = cacheService;
        _analyzerService = analyzerService;
        _unitOfWork = unitOfWork;
        _progressReporter = progressReporter;
        _workTracker = workTracker;
        _contributorService = contributorService;
        _distributedCache = distributedCache;
        _releaseNotesRenderer = releaseNotesRenderer;
        _liveUpdates = liveUpdates;
        _lookups = lookups;
        _jobs = jobs;
        _logger = logger;
    }

    public async Task<AnalysisResult> AnalyzePackageAsync(AnalyzeRequest request, string ipAddress)
    {
        // The key is taken before the work fills in missing versions on the request
        var workKey = ipAddress == "System" ? WorkKeys.Refresh(request) : WorkKeys.Analysis(request);

        return await _workTracker.RunCoalescedAsync(workKey, () => PkgDiffTelemetry.MeasureAnalysisAsync(request.Registry, request.PackageId, async () =>
        {
            var startTime = DateTime.UtcNow;
            var connectionId = request.ConnectionId;
            var registry = _registryFactory.GetClient(request.Registry);

            await SendProgress(connectionId, $"Initializing analysis for {request.PackageId}...");

            await ValidatePackageExistsAsync(request, registry);
            var latestRequested = string.IsNullOrEmpty(request.TargetVersion);
            await AutoSelectVersionsIfNeeded(request, registry);
            var (fromVersion, toVersion) = ParseVersions(request, registry);

            await SendProgress(connectionId, "Calculating version differences...");
            var versionsInRange = await registry.GetVersionsInRangeAsync(
                request.PackageId, fromVersion, toVersion, request.IncludePrerelease);

            var cachedPackage = await _cacheService.GetOrCreateCachedPackageAsync(
                request.PackageId, request.Registry, toVersion.ToString(),
                versionsInRange.Select(v => v.ToString()));

            var versionInfoList = await ProcessVersionsAsync(
                request.PackageId, versionsInRange, cachedPackage, registry, request.Registry, connectionId);

            await SendProgress(connectionId, "Generating final report...");
            var summary = _analyzerService.GenerateSummary(versionInfoList);

            await SaveSearchHistoryAsync(request, versionInfoList, startTime, ipAddress);
            // A visitor-picked version must not become news; it may only date the stored latest
            var isStoredLatest = registry.TryParseVersion(cachedPackage.LatestVersion, out var storedLatest) && storedLatest.Equals(toVersion);
            if (latestRequested || isStoredLatest)
            {
                var published = versionInfoList
                    .FirstOrDefault(v => registry.TryParseVersion(v.Version, out var parsed) && parsed.Equals(toVersion))?.PublishedDate;
                await _liveUpdates.RecordLatestVersionAsync(cachedPackage, request.Registry, request.TargetVersion, published);
            }
            await SendProgress(connectionId, "Analysis complete.");

            return new AnalysisResult
            {
                PackageId = request.PackageId,
                FromVersion = request.CurrentVersion,
                ToVersion = request.TargetVersion,
                TotalVersions = versionInfoList.Count,
                Versions = versionInfoList,
                Summary = summary
            };
        }));
    }

    public async Task<PackageMetadata> GetPackageMetadataAsync(string registry, string packageId)
    {
        return await _cacheService.GetOrFetchMetadataAsync(registry, packageId);
    }

    public async Task ScrapePackageReleaseNotesAsync(string packageId, string registry)
    {
        await _workTracker.RunCoalescedAsync(WorkKeys.Scrape(registry, packageId), async () =>
        {
            await _progressReporter.BroadcastPackageUpdateStartedAsync(packageId);

            try
            {
                var cachedPackage = await _cacheService.GetCachedPackageAsync(packageId, registry);
                if (cachedPackage == null) return false;

                string? githubRepoUrl = cachedPackage.GitHubRepositoryUrl;
                var registryClient = _registryFactory.GetClient(registry);

                // Newest versions only, so a package with thousands of them cannot hold a worker for hours
                var versions = (JsonSerializer.Deserialize<List<string>>(cachedPackage.AllVersionsJson) ?? [])
                    .Select(v => (Text: v, Parsed: registryClient.TryParseVersion(v, out var parsed) ? parsed : null))
                    .OrderByDescending(v => v.Parsed is not null)
                    .ThenByDescending(v => v.Parsed)
                    .Take(MaxScrapedVersions)
                    .Select(v => v.Text)
                    .ToList();
                var strategy = registryClient.GetScrapingStrategy(githubRepoUrl);

                int currentVersion = 0;
                int totalVersions = versions.Count;
                foreach (var versionString in versions)
                {
                    try
                    {
                        currentVersion++;
                        await _progressReporter.BroadcastBackgroundProgressAsync(packageId,
                            $"Updating {packageId} v{versionString} ({currentVersion}/{totalVersions})...");

                        bool discoveredNewGithubUrl = await ScrapeVersionReleaseNotesAsync(
                            cachedPackage, versionString, strategy, registryClient, githubRepoUrl);

                        if (discoveredNewGithubUrl)
                        {
                            githubRepoUrl = cachedPackage.GitHubRepositoryUrl;
                            strategy = registryClient.GetScrapingStrategy(githubRepoUrl);
                        }
                    }
                    catch (GitHubRateLimitedException ex)
                    {
                        // The scrape resumes once the limit resets, skipping the versions already done
                        var delay = ex.ResetAt - DateTime.UtcNow + RateLimitResetMargin;
                        if (delay < RateLimitResetMargin)
                            delay = RateLimitResetMargin;

                        _jobs.Schedule<IPackageAnalysisService>(
                            service => service.ScrapePackageReleaseNotesAsync(packageId, registry), delay);
                        _logger.LogInformation(
                            "GitHub rate limit reached while scraping {PackageId} at v{Version}; continuing in {Delay}",
                            packageId, versionString, delay);
                        break;
                    }
                    catch (Exception ex)
                    {
                        _logger.LogWarning(ex, "Failed to scrape release notes for {PackageId} v{Version}, skipping",
                            packageId, versionString);

                        try
                        {
                            await SaveFailedScrapePlaceholderAsync(cachedPackage, versionString, registryClient);
                        }
                        catch (Exception saveEx)
                        {
                            _logger.LogError(saveEx, "Failed to save failed scrape placeholder for {PackageId} v{Version}",
                                packageId, versionString);
                        }
                    }
                }
            }
            finally
            {
                await _progressReporter.BroadcastPackageUpdatedAsync(packageId);
            }
            return true;
        });
    }

    public async Task<List<PackageMetadata>> GetCachedPackagesAsync()
    {
        return await _unitOfWork.ToListAsync(_unitOfWork.CachedPackages.Query()
            .Where(p => p.LatestVersion != "")
            .OrderByDescending(p => p.CachedAt)
            .Take(MaxListedPackages)
            .Select(p => new PackageMetadata
            {
                PackageId = p.PackageId,
                Registry = p.Registry.Value,
                LatestVersion = p.LatestVersion,
                Description = p.Description,
                Authors = p.Authors,
                IconUrl = p.IconUrl,
                ProjectUrl = p.ProjectUrl,
                GitHubRepositoryUrl = p.GitHubRepositoryUrl
            }));
    }

    public async Task<List<RegistryInfo>> GetRegistriesAsync()
    {
        var dbRegistries = await _unitOfWork.Registries.GetAllAsync();
        return dbRegistries
            .OrderBy(r => r.DisplayOrder)
            .Select(r => new RegistryInfo
            {
                Value = r.Value,
                Label = r.Label,
                Icon = r.Icon,
                Disabled = r.Disabled
            })
            .ToList();
    }

    public async Task<List<PackageSuggestion>> SearchSuggestionsAsync(string registry, string query, int limit = 5)
    {
        var registryId = await _cacheService.ResolveRegistryIdAsync(registry);
        var term = query.Trim().ToLowerInvariant();

        return await _unitOfWork.ToListAsync(_unitOfWork.CachedPackages.Query()
            .Where(p => p.RegistryId == registryId && p.PackageId.ToLower().Contains(term))
            .OrderBy(p => p.PackageId.Length)
            .ThenBy(p => p.PackageId)
            .Take(limit)
            .Select(p => new PackageSuggestion
            {
                PackageId = p.PackageId,
                Registry = registry,
                LatestVersion = p.LatestVersion,
                Description = p.Description,
                IconUrl = p.IconUrl
            }));
    }

    private async Task SendProgress(string? connectionId, string message)
    {
        await _progressReporter.SendProgressAsync(connectionId, message);
    }

    private static async Task ValidatePackageExistsAsync(AnalyzeRequest request, IRegistryClient registry)
    {
        bool exists = await registry.PackageExistsAsync(request.PackageId, request.IncludePrerelease);
        if (!exists)
            throw new ArgumentException($"Package '{request.PackageId}' not found in {request.Registry} registry.");
    }

    private async Task AutoSelectVersionsIfNeeded(AnalyzeRequest request, IRegistryClient registry)
    {
        if (!string.IsNullOrEmpty(request.CurrentVersion) && !string.IsNullOrEmpty(request.TargetVersion))
            return;

        await SendProgress(request.ConnectionId, "No versions provided. Fetching latest releases...");
        var allVersions = await registry.GetVersionsAsync(request.PackageId, request.IncludePrerelease);

        if (allVersions.Count < 2)
            throw new ArgumentException($"Package '{request.PackageId}' does not have enough versions for analysis.");

        request.TargetVersion = allVersions[^1].Value;
        request.CurrentVersion = allVersions[^2].Value;
        await SendProgress(request.ConnectionId, $"Selected latest versions: {request.CurrentVersion} -> {request.TargetVersion}");
    }

    private static (PackageVersion from, PackageVersion to) ParseVersions(AnalyzeRequest request, IRegistryClient registry)
    {
        if (!registry.TryParseVersion(request.CurrentVersion, out var fromVersion))
            throw new ArgumentException($"Invalid version format: {request.CurrentVersion}");
        if (!registry.TryParseVersion(request.TargetVersion, out var toVersion))
            throw new ArgumentException($"Invalid version format: {request.TargetVersion}");
        return (fromVersion, toVersion);
    }

    private async Task<List<VersionInfo>> ProcessVersionsAsync(
        string packageId,
        List<PackageVersion> versionsList,
        Domain.Entities.CachedPackage cachedPackage,
        IRegistryClient registryClient,
        string registry,
        string? connectionId)
    {
        var maxInitialVersions = await _lookups.GetIntSettingAsync("MaxVersionsToAnalyze", 50);
        var maxConsecutiveEmpty = await _lookups.GetIntSettingAsync("MaxConsecutiveEmptyVersions", 10);

        var versionsToProcess = versionsList.Take(maxInitialVersions).ToList();

        var versionStrings = versionsToProcess.Select(v => v.ToString()).ToList();
        var cachedNotes = (await _unitOfWork.CachedReleaseNotes
                .FindAsync(rn => rn.CachedPackageId == cachedPackage.Id && versionStrings.Contains(rn.Version)))
            .GroupBy(rn => rn.Version)
            .ToDictionary(g => g.Key, g => g.First());
        int total = versionsList.Count;

        if (total > maxInitialVersions)
            await SendProgress(connectionId, $"Processing {maxInitialVersions} most recent versions (total: {total})...");

        var versionInfoList = new List<VersionInfo>();
        int consecutiveEmpty = 0;
        int current = 0;

        foreach (var version in versionsToProcess)
        {
            WorkCancellation.Token.ThrowIfCancellationRequested();
            current++;
            var versionString = version.ToString();
            await SendProgress(connectionId, $"Processing {packageId} v{versionString} ({current}/{total})...");

            cachedNotes.TryGetValue(versionString, out var cachedNote);

            string releaseNotes;
            string releaseNotesSource;
            DateTime? publishedDate;
            bool isFromCache = false;

            if (cachedNote != null)
            {
                releaseNotes = cachedNote.ReleaseNotes;
                releaseNotesSource = cachedNote.ReleaseNotesSource ?? registryClient.SourceLabel;
                publishedDate = cachedNote.PublishedDate;
                isFromCache = true;

                if (cachedNote.ExpiresAt <= DateTime.UtcNow)
                {
                    _logger.LogDebug("[PackageAnalysis] Release note expired for {PackageId} v{Version}, triggering background scrape.", packageId, versionString);
                    await TriggerBackgroundScrapingAsync(packageId, registry);
                }
            }
            else
            {
                releaseNotes = string.Empty;
                releaseNotesSource = registryClient.SourceLabel;
                publishedDate = await registryClient.GetPublishedDateAsync(packageId, versionString);
                isFromCache = false;

                _logger.LogDebug("[PackageAnalysis] No cache found for {PackageId} v{Version}, triggering background scrape.", packageId, versionString);
                await TriggerBackgroundScrapingAsync(packageId, registry);
            }

            var versionInfo = CreateVersionInfo(version, releaseNotes, releaseNotesSource, publishedDate, cachedNote, isFromCache);
            versionInfoList.Add(versionInfo);

            if (versionInfo.IsEmpty)
            {
                consecutiveEmpty++;
                if (consecutiveEmpty >= maxConsecutiveEmpty)
                {
                    await SendProgress(connectionId, $"Stopping: {maxConsecutiveEmpty} consecutive versions without release notes.");
                    break;
                }
            }
            else
            {
                consecutiveEmpty = 0;
            }
        }

        return versionInfoList;
    }

    private async Task TriggerBackgroundScrapingAsync(string packageId, string registry)
    {
        var cacheKey = $"scrape-attempt-{registry}-{packageId}";
        if (!_workTracker.IsRunning(WorkKeys.Scrape(registry, packageId)))
        {
            if (await _distributedCache.GetAsync(cacheKey) == null)
            {
                await _distributedCache.SetAsync(cacheKey, new byte[] { 1 },
                    new DistributedCacheEntryOptions
                    {
                        AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(12)
                    });
                
                _jobs.Enqueue<IPackageAnalysisService>(
                    service => service.ScrapePackageReleaseNotesAsync(packageId, registry));
            }
        }
    }

    private async Task<bool> ScrapeVersionReleaseNotesAsync(
        Domain.Entities.CachedPackage cachedPackage,
        string versionString,
        IReleaseNotesScrapingStrategy strategy,
        IRegistryClient registry,
        string? githubRepoUrl)
    {
        var cachedNote = await _cacheService.GetCachedReleaseNoteAsync(cachedPackage.Id, versionString);

        // A row seeded from the version list has no source yet: it was never looked up
        var now = DateTime.UtcNow;
        if (cachedNote != null && cachedNote.ExpiresAt > now
            && (!cachedNote.IsEmpty
                || (!string.IsNullOrEmpty(cachedNote.ReleaseNotesSource) && cachedNote.CachedAt > now - EmptyNotesRetryAfter)))
            return false;

        using var activity = PkgDiffTelemetry.StartScrape(registry.Id, cachedPackage.PackageId, versionString);
        var result = await strategy.GetReleaseNotesAsync(cachedPackage.PackageId, versionString, githubRepoUrl, registry);
        var releaseNotes = StoredMarkdown.Normalize(result.ReleaseNotes);
        PkgDiffTelemetry.RecordScrape(activity, registry.Id, result.Source, !string.IsNullOrWhiteSpace(releaseNotes));

        if (result.DiscoveredGitHubUrl != null)
        {
            cachedPackage.GitHubRepositoryUrl = result.DiscoveredGitHubUrl;
            cachedPackage.ReleaseNotesSource = "GitHub";
            await _cacheService.UpdateCachedPackageAsync(cachedPackage);
        }

        var publishedDate = cachedNote?.PublishedDate
            ?? await registry.GetPublishedDateAsync(cachedPackage.PackageId, versionString);

        var parsedJson = JsonSerializer.Serialize(
            _analyzerService.ParseReleaseNotes(CompiledReleaseNotes.Split(releaseNotes).Body));

        if (cachedNote == null)
        {
            cachedNote = new Domain.Entities.CachedReleaseNote
            {
                CachedPackageId = cachedPackage.Id,
                Version = versionString,
                ReleaseNotes = releaseNotes,
                ParsedNotesJson = parsedJson,
                ReleaseNotesSource = result.Source,
                PublishedDate = publishedDate,
                IsEmpty = string.IsNullOrWhiteSpace(releaseNotes),
                CachedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(30)
            };
        }
        else
        {
            cachedNote.ReleaseNotes = releaseNotes;
            cachedNote.ParsedNotesJson = parsedJson;
            cachedNote.ReleaseNotesSource = result.Source;
            cachedNote.PublishedDate = publishedDate;
            cachedNote.IsEmpty = string.IsNullOrWhiteSpace(releaseNotes);
            cachedNote.CachedAt = DateTime.UtcNow;
            cachedNote.ExpiresAt = DateTime.UtcNow.AddDays(30);
        }

        await _cacheService.SaveReleaseNoteAsync(cachedNote);

        if (result.Source == "GitHub" && !string.IsNullOrWhiteSpace(releaseNotes))
        {
            try
            {
                var mentions = _analyzerService.ExtractMentions(releaseNotes);
                if (mentions.Any())
                    await _contributorService.UpsertContributorMentionsAsync(mentions);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "[PackageAnalysis] Failed to update contributor mentions during background scrape");
            }
        }

        return result.DiscoveredGitHubUrl != null;
    }

    private VersionInfo CreateVersionInfo(
        PackageVersion version,
        string? releaseNotes,
        string releaseNotesSource,
        DateTime? publishedDate,
        Domain.Entities.CachedReleaseNote? cachedReleaseNote,
        bool isFromCache)
    {
        var notes = releaseNotes ?? string.Empty;
        var (body, compiledFrom) = CompiledReleaseNotes.Split(notes);
        List<ReleaseNoteEntry> entries;

        if (isFromCache && !string.IsNullOrWhiteSpace(cachedReleaseNote?.ParsedNotesJson))
        {
            entries = JsonSerializer.Deserialize<List<ReleaseNoteEntry>>(
                cachedReleaseNote.ParsedNotesJson) ?? _analyzerService.ParseReleaseNotes(body);
        }
        else
        {
            entries = _analyzerService.ParseReleaseNotes(body);
        }
        entries.RemoveAll(e => CompiledReleaseNotes.IsNotice(e.Text));

        foreach (var entry in entries)
            entry.TextHtml = _releaseNotesRenderer.RenderHtml(entry.Text);

        return new VersionInfo
        {
            Version = version.ToString(),
            PublishedDate = publishedDate,
            ReleaseNotes = notes,
            ReleaseNotesHtml = _releaseNotesRenderer.RenderHtml(body),
            ReleaseNotesSource = releaseNotesSource,
            CompiledFrom = compiledFrom,
            Entries = entries,
            IsEmpty = string.IsNullOrWhiteSpace(notes),
            IsPrerelease = version.IsPrerelease,
            Categories = entries.Select(e => e.Category).Distinct().ToList()
        };
    }

    private async Task SaveSearchHistoryAsync(
        AnalyzeRequest request,
        List<VersionInfo> versionInfoList,
        DateTime startTime,
        string ipAddress)
    {
        if (ipAddress == "System")
        {
            return;
        }

        var searchHistory = new Domain.Entities.SearchHistory
        {
            PackageId = request.PackageId,
            FromVersion = request.CurrentVersion,
            ToVersion = request.TargetVersion,
            IncludePrerelease = request.IncludePrerelease,
            TotalVersionsAnalyzed = versionInfoList.Count,
            SearchedAt = DateTime.UtcNow,
            IpAddress = ipAddress,
            ProcessingTime = DateTime.UtcNow - startTime
        };

        await _unitOfWork.SearchHistories.AddAsync(searchHistory);
        await _unitOfWork.SaveChangesAsync();
    }

    private async Task SaveFailedScrapePlaceholderAsync(Domain.Entities.CachedPackage cachedPackage, string versionString, IRegistryClient registry)
    {
        var cachedNote = await _cacheService.GetCachedReleaseNoteAsync(cachedPackage.Id, versionString);
        if (cachedNote == null)
        {
            cachedNote = new Domain.Entities.CachedReleaseNote
            {
                CachedPackageId = cachedPackage.Id,
                Version = versionString,
                ReleaseNotes = string.Empty,
                ParsedNotesJson = "[]",
                ReleaseNotesSource = registry.SourceLabel,
                PublishedDate = null,
                IsEmpty = true,
                CachedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            };
            await _cacheService.SaveReleaseNoteAsync(cachedNote);
        }
    }
}
