using System.Text.Json;
using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Domain.Common;
using PkgDiff.Domain.Entities;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Shared.Constants;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Services;

// Security data is never fetched while a visitor waits: a request reads what is stored and, when
// that is missing or old, queues a background refresh. The refresh asks the providers, stores the
// result and tells the open pages over SignalR.
public class PackageTrustService : IPackageTrustService
{
    private const int DefaultCacheHours = 24;
    private const int MinCacheHours = 1;
    private const int MaxCacheHours = 168;
    // OSV answers up to 1000 queries per batch; a package with more versions is checked for its newest 1000
    private const int MaxVersionsChecked = 1000;
    private const int ExpiredPerRun = 100;
    private const int MaxErrorLength = 1000;
    // One refresh per package is queued at most this often, however many visitors open it
    private static readonly TimeSpan RequeueAfter = TimeSpan.FromMinutes(10);
    // A failed refresh is tried again after this long instead of on the next visit
    private static readonly TimeSpan RetryAfterFailure = TimeSpan.FromHours(1);

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IUnitOfWork _unitOfWork;
    private readonly ISystemSettingsReader _settings;
    private readonly IPackageCacheService _cache;
    private readonly IOsvClient _osv;
    private readonly IPackageHealthClient _health;
    private readonly IBehaviorClient _behavior;
    private readonly IJobScheduler _jobs;
    private readonly IProgressReporter _progress;
    private readonly IWorkTrackerService _workTracker;
    private readonly ILogger<PackageTrustService> _logger;

    public PackageTrustService(
        IUnitOfWork unitOfWork,
        ISystemSettingsReader settings,
        IPackageCacheService cache,
        IOsvClient osv,
        IPackageHealthClient health,
        IBehaviorClient behavior,
        IJobScheduler jobs,
        IProgressReporter progress,
        IWorkTrackerService workTracker,
        ILogger<PackageTrustService> logger)
    {
        _unitOfWork = unitOfWork;
        _settings = settings;
        _cache = cache;
        _osv = osv;
        _health = health;
        _behavior = behavior;
        _jobs = jobs;
        _progress = progress;
        _workTracker = workTracker;
        _logger = logger;
    }

    public async Task<PackageTrust> GetTrustAsync(string registry, string packageId)
    {
        var canonical = RegistryNames.Canonical(registry) ?? registry.ToLowerInvariant();
        var key = packageId.ToLowerInvariant();

        var osvEnabled = await IsEnabledAsync(SettingKeys.SecurityOsvEnabled);
        var healthEnabled = await IsEnabledAsync(SettingKeys.SecurityDepsDevEnabled);
        if (!osvEnabled && !healthEnabled)
            return new PackageTrust { Status = TrustStatus.Unavailable };

        var row = await FindAsync(canonical, key);
        var now = DateTime.UtcNow;
        if (row?.ExpiresAt is null || row.ExpiresAt <= now)
            await QueueRefreshAsync(row, canonical, key, packageId, now);

        if (row?.FetchedAt is null)
        {
            return new PackageTrust
            {
                Status = row?.LastError is null ? TrustStatus.Pending : TrustStatus.Unavailable
            };
        }

        var trust = Read(row.DataJson);
        trust.Status = TrustStatus.Ready;
        trust.FetchedAt = row.FetchedAt;
        if (!osvEnabled)
        {
            trust.Advisories.Clear();
            trust.VersionAdvisories.Clear();
        }
        if (!healthEnabled)
            trust.Health = null;
        if (!await IsEnabledAsync(SettingKeys.SecuritySocketEnabled))
            trust.Behavior = null;
        return trust;
    }

    public async Task RefreshAsync(string registry, string packageId)
    {
        await _workTracker.RunCoalescedAsync(WorkKeys.Security(registry, packageId), async () =>
        {
            await RefreshNowAsync(registry, packageId);
            return true;
        });
    }

    public async Task RefreshExpiredAsync()
    {
        var now = DateTime.UtcNow;
        var expired = await _unitOfWork.ToListAsync(_unitOfWork.PackageSecurityInfos.Query()
            .Where(x => x.FetchedAt != null && x.ExpiresAt != null && x.ExpiresAt <= now)
            .OrderBy(x => x.ExpiresAt)
            .Select(x => new { x.Registry, x.PackageId })
            .Take(ExpiredPerRun));

        foreach (var item in expired)
        {
            try
            {
                await RefreshAsync(item.Registry, item.PackageId);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Security refresh of {Registry} {PackageId} failed", item.Registry, item.PackageId);
            }
        }
    }

    private async Task RefreshNowAsync(string registry, string packageId)
    {
        var canonical = RegistryNames.Canonical(registry) ?? registry.ToLowerInvariant();
        var key = packageId.ToLowerInvariant();

        var cached = await _cache.GetCachedPackageAsync(packageId, canonical);
        if (cached is null)
        {
            _logger.LogInformation("Security refresh skipped: {Registry} {PackageId} is not cached", canonical, packageId);
            return;
        }

        var row = await FindAsync(canonical, key);
        var previous = row?.FetchedAt is null ? new PackageTrust() : Read(row.DataJson);
        var trust = new PackageTrust
        {
            Advisories = previous.Advisories,
            VersionAdvisories = previous.VersionAdvisories,
            Health = previous.Health,
            Behavior = previous.Behavior
        };
        var errors = new List<string>();

        if (await IsEnabledAsync(SettingKeys.SecurityOsvEnabled))
        {
            try
            {
                var versions = (JsonSerializer.Deserialize<List<string>>(cached.AllVersionsJson) ?? [])
                    .Where(v => !string.IsNullOrWhiteSpace(v))
                    .Distinct(StringComparer.Ordinal)
                    .Take(MaxVersionsChecked)
                    .ToList();
                var report = await _osv.QueryAsync(canonical, cached.PackageId, versions);
                trust.Advisories = report.Advisories;
                trust.VersionAdvisories = report.VersionAdvisories;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "OSV lookup failed for {Registry} {PackageId}", canonical, cached.PackageId);
                errors.Add("OSV: " + ex.Message);
            }
        }

        if (await IsEnabledAsync(SettingKeys.SecurityDepsDevEnabled))
        {
            try
            {
                trust.Health = await _health.GetHealthAsync(
                    canonical, cached.PackageId, cached.LatestVersion, cached.GitHubRepositoryUrl ?? cached.ProjectUrl);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Health lookup failed for {Registry} {PackageId}", canonical, cached.PackageId);
                errors.Add("Health: " + ex.Message);
            }
        }

        if (await IsEnabledAsync(SettingKeys.SecuritySocketEnabled))
        {
            try
            {
                var newest = (JsonSerializer.Deserialize<List<string>>(cached.AllVersionsJson) ?? []).ToList();
                var reports = await _behavior.GetReportsAsync(canonical, cached.PackageId, newest);
                // An empty answer (budget used up, no token) keeps what was stored before
                if (reports.Count > 0)
                {
                    var merged = new Dictionary<string, BehaviorReport>(trust.Behavior ?? new(), StringComparer.Ordinal);
                    foreach (var (version, report) in reports)
                        merged[version] = report;
                    trust.Behavior = merged;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Socket lookup failed for {Registry} {PackageId}", canonical, cached.PackageId);
                errors.Add("Socket: " + ex.Message);
            }
        }

        var now = DateTime.UtcNow;
        row ??= await AddRowAsync(canonical, key, packageId);
        var everythingFailed = errors.Count > 0 && trust.Advisories.Count == 0 && trust.VersionAdvisories.Count == 0 && trust.Health is null;
        if (everythingFailed)
        {
            row.LastError = Trim(string.Join("; ", errors));
            row.ExpiresAt = now + RetryAfterFailure;
        }
        else
        {
            row.DataJson = JsonSerializer.Serialize(trust, Json);
            row.FetchedAt = now;
            row.ExpiresAt = now + TimeSpan.FromHours(await CacheHoursAsync());
            // A provider that failed this time is asked again within the hour
            if (errors.Count > 0)
            {
                row.LastError = Trim(string.Join("; ", errors));
                row.ExpiresAt = now + RetryAfterFailure;
            }
            else
            {
                row.LastError = null;
            }
        }

        await _unitOfWork.PackageSecurityInfos.UpdateAsync(row);
        await _unitOfWork.SaveChangesAsync();

        if (!everythingFailed)
            await _progress.BroadcastSecurityUpdatedAsync(packageId);
    }

    private async Task QueueRefreshAsync(PackageSecurityInfo? row, string registry, string key, string packageId, DateTime now)
    {
        if (row?.RequestedAt is { } requested && now - requested < RequeueAfter)
            return;
        if (_workTracker.IsRunning(WorkKeys.Security(registry, packageId)))
            return;

        try
        {
            row ??= await AddRowAsync(registry, key, packageId);
            row.RequestedAt = now;
            await _unitOfWork.PackageSecurityInfos.UpdateAsync(row);
            await _unitOfWork.SaveChangesAsync();
            _jobs.Enqueue<IPackageTrustService>(service => service.RefreshAsync(registry, packageId));
        }
        catch (DuplicateKeyException)
        {
            // Another request created the row and queued the refresh a moment ago
        }
        catch (Exception ex)
        {
            // The page is still served; the next visit tries again
            _logger.LogWarning(ex, "Could not queue a security refresh for {Registry} {PackageId}", registry, packageId);
        }
    }

    private async Task<PackageSecurityInfo> AddRowAsync(string registry, string key, string packageId)
    {
        var row = new PackageSecurityInfo { Registry = registry, PackageKey = key, PackageId = packageId, DataJson = "{}" };
        await _unitOfWork.PackageSecurityInfos.AddAsync(row);
        await _unitOfWork.SaveChangesAsync();
        return row;
    }

    private async Task<PackageSecurityInfo?> FindAsync(string registry, string key) =>
        (await _unitOfWork.PackageSecurityInfos.FindAsync(x => x.Registry == registry && x.PackageKey == key)).FirstOrDefault();

    private static PackageTrust Read(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<PackageTrust>(json, Json) ?? new PackageTrust();
        }
        catch (JsonException)
        {
            return new PackageTrust();
        }
    }

    private async Task<bool> IsEnabledAsync(string key)
    {
        var value = await _settings.GetAsync(key);
        var defaultValue = SettingKeys.Find(key)?.DefaultValue ?? "false";
        return bool.TryParse(value ?? defaultValue, out var enabled) && enabled;
    }

    private async Task<int> CacheHoursAsync()
    {
        var value = await _settings.GetAsync(SettingKeys.SecurityCacheHours);
        return int.TryParse(value, out var hours) ? Math.Clamp(hours, MinCacheHours, MaxCacheHours) : DefaultCacheHours;
    }

    private static string Trim(string text) => text.Length <= MaxErrorLength ? text : text[..MaxErrorLength];
}
