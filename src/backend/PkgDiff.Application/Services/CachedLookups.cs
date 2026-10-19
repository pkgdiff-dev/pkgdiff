using Microsoft.Extensions.Caching.Memory;
using PkgDiff.Application.Interfaces;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Shared.Constants;

namespace PkgDiff.Application.Services;

// Small lookups that every analysis repeats. A change made in the admin panel shows after the lifetime at the latest.
public class CachedLookups : ICachedLookups
{
    private static readonly TimeSpan RegistryLifetime = TimeSpan.FromMinutes(10);
    private static readonly TimeSpan SettingLifetime = TimeSpan.FromMinutes(1);

    private sealed record CachedSetting(int? Value);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IMemoryCache _cache;

    public CachedLookups(IUnitOfWork unitOfWork, IMemoryCache cache)
    {
        _unitOfWork = unitOfWork;
        _cache = cache;
    }

    public async Task<int> GetRegistryIdAsync(string registryValue)
    {
        var value = registryValue.ToLowerInvariant();
        var cacheKey = $"registry-id:{RegistryNames.Canonical(value) ?? value}";
        if (_cache.TryGetValue(cacheKey, out int cachedId))
            return cachedId;

        // The row may be stored under any spelling of the registry: pub.dev's is "pub"
        var spellings = RegistryNames.SpellingsOf(value);
        var registry = (await _unitOfWork.Registries.FindAsync(r => spellings.Contains(r.Value))).FirstOrDefault();
        if (registry == null)
            throw new ArgumentException($"Registry '{registryValue}' not found in the database.");

        _cache.Set(cacheKey, registry.Id, RegistryLifetime);
        return registry.Id;
    }

    public async Task<int> GetIntSettingAsync(string key, int defaultValue)
    {
        var cacheKey = $"setting-int:{key}";
        if (!_cache.TryGetValue(cacheKey, out CachedSetting? cached) || cached == null)
        {
            var setting = (await _unitOfWork.SystemSettings.FindAsync(s => s.Key == key)).FirstOrDefault();
            cached = new CachedSetting(setting != null && int.TryParse(setting.Value, out var parsed) ? parsed : null);
            _cache.Set(cacheKey, cached, SettingLifetime);
        }

        return cached.Value ?? defaultValue;
    }
}
