namespace PkgDiff.Domain.Interfaces;

public interface IVersionFilterService
{
    /// <summary>
    /// Gets all active exclude patterns from database (cached).
    /// </summary>
    Task<IEnumerable<string>> GetActiveExcludePatternsAsync();

    /// <summary>
    /// Checks if a version should be excluded based on patterns.
    /// </summary>
    bool ShouldExcludeVersion(string version, IEnumerable<string> patterns);

    /// <summary>
    /// Refreshes the cached patterns from database.
    /// </summary>
    Task RefreshPatternsAsync();
}
