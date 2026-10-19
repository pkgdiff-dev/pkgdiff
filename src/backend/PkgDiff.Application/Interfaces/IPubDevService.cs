using PkgDiff.Application.Interfaces;

namespace PkgDiff.Application.Interfaces;

public interface IPubDevService : IScraperService
{
    Task<(string? Publisher, string? Description, string? RepositoryUrl)> GetPackageExtraInfoAsync(string packageId);
    Task<List<(string Version, DateTime? PublishDate)>> GetAllVersionsAsync(string packageId);
}
