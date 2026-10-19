namespace PkgDiff.Domain.Interfaces;

public interface IVersionFilterService
{
    Task<IEnumerable<string>> GetActiveExcludePatternsAsync();

    bool ShouldExcludeVersion(string version, IEnumerable<string> patterns);

    Task RefreshPatternsAsync();
}
