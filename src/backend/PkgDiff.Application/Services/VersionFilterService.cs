using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using PkgDiff.Domain.Interfaces;

namespace PkgDiff.Application.Services;

public class VersionFilterService : IVersionFilterService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IMemoryCache _cache;
    private readonly ILogger<VersionFilterService> _logger;
    private const string CacheKey = "VersionExcludePatterns";
    private static readonly TimeSpan CacheExpiration = TimeSpan.FromMinutes(5);

    public VersionFilterService(IUnitOfWork unitOfWork, IMemoryCache cache, ILogger<VersionFilterService> logger)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
        _logger = logger;
    }

    public async Task<IEnumerable<string>> GetActiveExcludePatternsAsync()
    {
        // Try to get from cache first
        if (_cache.TryGetValue(CacheKey, out IEnumerable<string>? cachedPatterns) && cachedPatterns != null)
        {
            _logger.LogDebug("[VersionFilter] Using cached patterns: {Patterns}", string.Join(", ", cachedPatterns));
            return cachedPatterns;
        }

        // Fetch from database
        var patterns = await _unitOfWork.VersionExcludePatterns.GetAllAsync();
        var activePatterns = patterns
            .Where(p => p.IsActive)
            .Select(p => p.Pattern)
            .ToList();

        _logger.LogDebug("[VersionFilter] Loaded {Count} active patterns from DB: {Patterns}",
            activePatterns.Count, string.Join(", ", activePatterns));

        // Cache the result
        _cache.Set(CacheKey, activePatterns, CacheExpiration);

        return activePatterns;
    }

    public bool ShouldExcludeVersion(string version, IEnumerable<string> patterns)
    {
        if (string.IsNullOrWhiteSpace(version))
            return false;

        // Check if version contains any of the exclude patterns (case-insensitive)
        var shouldExclude = patterns.Any(pattern =>
            version.Contains(pattern, StringComparison.OrdinalIgnoreCase));

        if (shouldExclude)
        {
            _logger.LogDebug("[VersionFilter] Excluding version: {Version}", version);
        }

        return shouldExclude;
    }

    public async Task RefreshPatternsAsync()
    {
        // Remove from cache to force refresh on next request
        _cache.Remove(CacheKey);

        // Pre-load the cache
        await GetActiveExcludePatternsAsync();
    }
}
