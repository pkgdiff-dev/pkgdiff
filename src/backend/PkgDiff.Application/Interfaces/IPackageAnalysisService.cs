using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Interfaces
{
    public interface IPackageAnalysisService
    {
        Task<AnalysisResult> AnalyzePackageAsync(AnalyzeRequest request, string ipAddress = "System");
        Task<PackageMetadata> GetPackageMetadataAsync(string registry, string packageId);
        Task ScrapePackageReleaseNotesAsync(string packageId, string registry);
        Task<List<RegistryInfo>> GetRegistriesAsync();
        Task<List<PackageMetadata>> GetCachedPackagesAsync();
        Task<List<PackageSuggestion>> SearchSuggestionsAsync(string registry, string query, int limit = 5);
    }
}
