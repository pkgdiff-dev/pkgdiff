using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Interfaces;

public interface IAdminPackageService
{
    Task<PaginatedResult<AdminPackageInfo>> GetPackagesAsync(int page = 1, int pageSize = 20, string? search = null);
    Task DeletePackageAsync(int id);

    Task<List<AdminRegistryInfo>> GetRegistriesAsync();
    Task<AdminRegistryInfo> CreateRegistryAsync(RegistryCreateRequest request);
    Task<AdminRegistryInfo> UpdateRegistryAsync(int id, RegistryUpdateRequest request);
    Task DeleteRegistryAsync(int id);
    Task ReorderRegistriesAsync(List<RegistryReorderRequest> requests);

    Task<List<SitemapEntry>> GetPackagesForSitemapAsync();
}
