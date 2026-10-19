using System.Diagnostics.CodeAnalysis;
using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Versioning;
using PkgDiff.Domain.Entities;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Services;

public class LiveUpdateService : ILiveUpdateService
{
    private const int MaxPageSize = 50;
    private const int MaxVersionLength = 50;
    private static readonly DateTime EarliestPlausibleDate = new(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IRegistryServiceFactory _registryFactory;
    private readonly IProgressReporter _progressReporter;
    private readonly ILogger<LiveUpdateService> _logger;

    public LiveUpdateService(
        IUnitOfWork unitOfWork,
        IRegistryServiceFactory registryFactory,
        IProgressReporter progressReporter,
        ILogger<LiveUpdateService> logger)
    {
        _unitOfWork = unitOfWork;
        _registryFactory = registryFactory;
        _progressReporter = progressReporter;
        _logger = logger;
    }

    public async Task<PaginatedResult<LiveUpdate>> GetLiveUpdatesAsync(int page = 1, int pageSize = 20)
    {
        page = Math.Max(page, 1);
        pageSize = Math.Clamp(pageSize, 1, MaxPageSize);

        // Packages without a LatestVersionDate are ordered by cache time until a refresh fills it in
        var (items, total) = await _unitOfWork.CachedPackages.GetPageDescendingAsync(
            p => p.LatestVersion != "",
            p => p.LatestVersionDate ?? p.CachedAt,
            p => new LiveUpdate
            {
                PackageId = p.PackageId,
                Registry = p.Registry.Value,
                Version = p.LatestVersion,
                PreviousVersion = p.PreviousLatestVersion,
                UpdatedAt = p.LatestVersionDate ?? p.CachedAt
            },
            page,
            pageSize);

        // Stored without a time zone; every value written is UTC
        foreach (var item in items)
            item.UpdatedAt = DateTime.SpecifyKind(item.UpdatedAt, DateTimeKind.Utc);

        return new PaginatedResult<LiveUpdate>
        {
            Data = items,
            Page = page,
            PageSize = pageSize,
            Total = total,
            TotalPages = (int)Math.Ceiling(total / (double)pageSize)
        };
    }

    public async Task<bool> RecordLatestVersionAsync(
        CachedPackage package,
        string registry,
        string latestVersion,
        DateTime? publishedDate,
        bool authoritative = false)
    {
        if (string.IsNullOrWhiteSpace(latestVersion) || latestVersion.Length > MaxVersionLength)
            return false;

        var date = ToPlausibleUtc(publishedDate);
        var current = package.LatestVersion;

        if (SameVersion(registry, current, latestVersion))
        {
            if (package.LatestVersionDate == null && date != null)
            {
                package.LatestVersionDate = date;
                await SaveAsync(package);
            }
            return false;
        }

        if (IsOlder(registry, latestVersion, current))
        {
            // Only the registry's own "latest" may move a package back (an unlisted release), and that is not news
            if (!authoritative)
                return false;

            package.LatestVersion = latestVersion;
            package.PreviousLatestVersion = null;
            package.LatestVersionDate = date ?? package.LatestVersionDate;
            await SaveAsync(package);
            return false;
        }

        var hadVersion = !string.IsNullOrWhiteSpace(current);
        package.PreviousLatestVersion = hadVersion ? current : null;
        package.LatestVersion = latestVersion;
        package.LatestVersionDate = date ?? DateTime.UtcNow;
        await SaveAsync(package);

        if (!hadVersion)
            return false;

        try
        {
            await _progressReporter.BroadcastLiveUpdateAsync(new LiveUpdate
            {
                PackageId = package.PackageId,
                Registry = registry.ToLowerInvariant(),
                Version = latestVersion,
                PreviousVersion = current,
                UpdatedAt = DateTime.SpecifyKind(package.LatestVersionDate.Value, DateTimeKind.Utc)
            });
        }
        catch (Exception ex)
        {
            // The version is stored; a visitor who misses the push still gets it from the feed
            _logger.LogWarning(ex, "Could not broadcast the new version {Version} of {PackageId}", latestVersion, package.PackageId);
        }

        return true;
    }

    private async Task SaveAsync(CachedPackage package)
    {
        await _unitOfWork.CachedPackages.UpdateAsync(package);
        await _unitOfWork.SaveChangesAsync();
    }

    private bool SameVersion(string registry, string current, string candidate)
    {
        if (TryParse(registry, current, out var currentVersion)
            && TryParse(registry, candidate, out var candidateVersion))
            return currentVersion.Equals(candidateVersion);

        return string.Equals(current, candidate, StringComparison.OrdinalIgnoreCase);
    }

    private bool IsOlder(string registry, string candidate, string current)
    {
        return TryParse(registry, current, out var currentVersion)
            && TryParse(registry, candidate, out var candidateVersion)
            && candidateVersion < currentVersion;
    }

    private bool TryParse(string registry, string value, [NotNullWhen(true)] out PackageVersion? version)
    {
        version = null;
        return _registryFactory.TryGetClient(registry, out var client) && client.TryParseVersion(value, out version);
    }

    // Registries report dates such as 1900-01-01 for unlisted packages; those are treated as unknown
    private static DateTime? ToPlausibleUtc(DateTime? value)
    {
        if (value == null)
            return null;

        var utc = value.Value.Kind == DateTimeKind.Local
            ? value.Value.ToUniversalTime()
            : DateTime.SpecifyKind(value.Value, DateTimeKind.Utc);

        if (utc < EarliestPlausibleDate || utc > DateTime.UtcNow.AddDays(1))
            return null;

        return utc;
    }
}
