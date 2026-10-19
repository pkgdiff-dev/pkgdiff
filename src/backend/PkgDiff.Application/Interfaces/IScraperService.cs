namespace PkgDiff.Application.Interfaces;

public interface IScraperService
{
    Task<string> GetReleaseNotesAsync(string packageId, string version);
    Task<string?> GetGitHubRepositoryUrlAsync(string packageId);
}
