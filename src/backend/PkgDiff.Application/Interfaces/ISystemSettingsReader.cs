namespace PkgDiff.Application.Interfaces;

public interface ISystemSettingsReader
{
    Task<string?> GetAsync(string key);
}
