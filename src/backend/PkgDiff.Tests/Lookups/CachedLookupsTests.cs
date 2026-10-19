using Microsoft.Extensions.Caching.Memory;
using PkgDiff.Application.Services;
using PkgDiff.Domain.Entities;
using PkgDiff.Tests.TestDoubles;

namespace PkgDiff.Tests.Lookups;

public class CachedLookupsTests
{
    private readonly FakeUnitOfWork _unitOfWork = new();
    private readonly CachedLookups _lookups;

    public CachedLookupsTests()
    {
        _lookups = new CachedLookups(_unitOfWork, new MemoryCache(new MemoryCacheOptions()));
    }

    [Fact]
    public async Task A_registry_id_is_read_once_and_then_served_from_the_cache()
    {
        var registry = await _unitOfWork.Registries.AddAsync(new Registry { Id = 4, Value = "pypi" });

        Assert.Equal(4, await _lookups.GetRegistryIdAsync("PyPI"));

        await _unitOfWork.Registries.DeleteAsync(registry);
        Assert.Equal(4, await _lookups.GetRegistryIdAsync("pypi"));
    }

    [Fact]
    public async Task An_unknown_registry_is_a_caller_fault_and_is_not_remembered()
    {
        await Assert.ThrowsAsync<ArgumentException>(() => _lookups.GetRegistryIdAsync("maven"));

        await _unitOfWork.Registries.AddAsync(new Registry { Id = 9, Value = "maven" });
        Assert.Equal(9, await _lookups.GetRegistryIdAsync("maven"));
    }

    [Fact]
    public async Task A_setting_falls_back_to_the_default_when_missing_or_not_a_number()
    {
        await _unitOfWork.SystemSettings.AddAsync(new SystemSettings { Key = "MaxVersionsToAnalyze", Value = "25" });
        await _unitOfWork.SystemSettings.AddAsync(new SystemSettings { Key = "Broken", Value = "many" });

        Assert.Equal(25, await _lookups.GetIntSettingAsync("MaxVersionsToAnalyze", 50));
        Assert.Equal(10, await _lookups.GetIntSettingAsync("Broken", 10));
        Assert.Equal(7, await _lookups.GetIntSettingAsync("Missing", 7));
        Assert.Equal(8, await _lookups.GetIntSettingAsync("Missing", 8));
    }

    [Fact]
    public async Task A_setting_is_served_from_the_cache_until_it_expires()
    {
        var setting = await _unitOfWork.SystemSettings.AddAsync(new SystemSettings { Key = "MaxVersionsToAnalyze", Value = "25" });
        Assert.Equal(25, await _lookups.GetIntSettingAsync("MaxVersionsToAnalyze", 50));

        setting.Value = "99";

        Assert.Equal(25, await _lookups.GetIntSettingAsync("MaxVersionsToAnalyze", 50));
    }
}
