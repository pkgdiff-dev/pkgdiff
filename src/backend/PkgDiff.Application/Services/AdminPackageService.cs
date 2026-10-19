using PkgDiff.Application.Interfaces;
using PkgDiff.Domain.Entities;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Shared.DTOs;
using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Identity;

namespace PkgDiff.Application.Services;

public class AdminPackageService : IAdminPackageService
{
    private readonly IUnitOfWork _unitOfWork;

    public AdminPackageService(IUnitOfWork unitOfWork)
    {
        _unitOfWork = unitOfWork;
    }

    public async Task<PaginatedResult<AdminPackageInfo>> GetPackagesAsync(int page = 1, int pageSize = 20, string? search = null)
    {
        (page, pageSize) = Paging.Clamp(page, pageSize);
        var term = search?.Trim().ToLowerInvariant() ?? string.Empty;

        var (packages, total) = await _unitOfWork.CachedPackages.GetPageDescendingAsync(
            p => term == "" || p.PackageId.ToLower().Contains(term),
            p => p.CachedAt,
            p => new AdminPackageInfo
            {
                Id = p.Id,
                PackageId = p.PackageId,
                LatestVersion = p.LatestVersion,
                ReleaseNotesSource = p.ReleaseNotesSource,
                ReleaseNotesCount = p.ReleaseNotes.Count,
                CachedAt = p.CachedAt,
                ExpiresAt = p.ExpiresAt
            },
            page,
            pageSize);

        return Paging.Result(packages, total, page, pageSize);
    }

    public async Task DeletePackageAsync(int id)
    {
        var entity = await _unitOfWork.CachedPackages.GetByIdAsync(id);
        if (entity == null) throw new KeyNotFoundException($"Package with id {id} not found");

        await _unitOfWork.CachedReleaseNotes.DeleteWhereAsync(r => r.CachedPackageId == id);

        await _unitOfWork.CachedPackages.DeleteAsync(entity);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task<List<AdminRegistryInfo>> GetRegistriesAsync()
    {
        var registries = await _unitOfWork.Registries.GetAllAsync();
        return registries
            .OrderBy(r => r.DisplayOrder)
            .Select(r => new AdminRegistryInfo
            {
                Id = r.Id,
                Value = r.Value,
                Label = r.Label,
                Icon = r.Icon,
                Disabled = r.Disabled,
                DisplayOrder = r.DisplayOrder,
                CreatedAt = r.CreatedAt
            })
            .ToList();
    }

    public async Task<AdminRegistryInfo> CreateRegistryAsync(RegistryCreateRequest request)
    {
        var entity = new Registry
        {
            Value = request.Value,
            Label = request.Label,
            Icon = request.Icon,
            Disabled = request.Disabled,
            DisplayOrder = request.DisplayOrder,
            CreatedAt = DateTime.UtcNow
        };

        var created = await _unitOfWork.Registries.AddAsync(entity);
        await _unitOfWork.SaveChangesAsync();

        return new AdminRegistryInfo
        {
            Id = created.Id,
            Value = created.Value,
            Label = created.Label,
            Icon = created.Icon,
            Disabled = created.Disabled,
            DisplayOrder = created.DisplayOrder,
            CreatedAt = created.CreatedAt
        };
    }

    public async Task<AdminRegistryInfo> UpdateRegistryAsync(int id, RegistryUpdateRequest request)
    {
        var entity = await _unitOfWork.Registries.GetByIdAsync(id);
        if (entity == null) throw new KeyNotFoundException($"Registry with id {id} not found");

        entity.Value = request.Value;
        entity.Label = request.Label;
        entity.Icon = request.Icon;
        entity.Disabled = request.Disabled;
        entity.DisplayOrder = request.DisplayOrder;

        await _unitOfWork.Registries.UpdateAsync(entity);
        await _unitOfWork.SaveChangesAsync();

        return new AdminRegistryInfo
        {
            Id = entity.Id,
            Value = entity.Value,
            Label = entity.Label,
            Icon = entity.Icon,
            Disabled = entity.Disabled,
            DisplayOrder = entity.DisplayOrder,
            CreatedAt = entity.CreatedAt
        };
    }

    public async Task DeleteRegistryAsync(int id)
    {
        var entity = await _unitOfWork.Registries.GetByIdAsync(id);
        if (entity == null) throw new KeyNotFoundException($"Registry with id {id} not found");
        await _unitOfWork.Registries.DeleteAsync(entity);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task ReorderRegistriesAsync(List<RegistryReorderRequest> requests)
    {
        foreach (var req in requests)
        {
            var entity = await _unitOfWork.Registries.GetByIdAsync(req.Id);
            if (entity == null) throw new KeyNotFoundException($"Registry with id {req.Id} not found");
            entity.DisplayOrder = req.DisplayOrder;
            await _unitOfWork.Registries.UpdateAsync(entity);
        }
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task<List<SitemapEntry>> GetPackagesForSitemapAsync()
    {
        return await _unitOfWork.ToListAsync(_unitOfWork.CachedPackages.Query()
            .OrderBy(p => p.Registry.Value)
            .ThenBy(p => p.PackageId)
            // A sitemap holds at most 50,000 addresses; the static pages and blog posts take the rest
            .Take(45_000)
            .Select(p => new SitemapEntry
            {
                PackageId = p.PackageId,
                Registry = p.Registry.Value,
                LastModified = p.CachedAt,
                ChangeFreq = "weekly",
                Priority = 0.6m
            }));
    }
}
