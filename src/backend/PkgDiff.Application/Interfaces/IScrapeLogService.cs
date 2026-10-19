namespace PkgDiff.Application.Interfaces;

public interface IScrapeLogService
{
    Task LogFailureAsync(
        string packageId,
        string version,
        string registry,
        string? githubRepoUrl,
        string failureReason,
        string? availableVersions,
        string attemptedSources);
}
