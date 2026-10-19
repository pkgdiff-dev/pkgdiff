namespace PkgDiff.Application.Interfaces;

public interface ICachedLookups
{
    Task<int> GetRegistryIdAsync(string registryValue);
    Task<int> GetIntSettingAsync(string key, int defaultValue);
}
