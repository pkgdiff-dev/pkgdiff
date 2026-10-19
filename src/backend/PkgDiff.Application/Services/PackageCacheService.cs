using System.Text.Json;
using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Domain.Common;
using PkgDiff.Domain.Entities;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Shared.DTOs;
using PkgDiff.Shared.ReleaseNotes;

namespace PkgDiff.Application.Services;

public class PackageCacheService : IPackageCacheService
{
    private readonly IUnitOfWork _unitOfWork;
    private readonly IRegistryServiceFactory _registryFactory;
    private readonly IReleaseNotesRenderer _releaseNotesRenderer;
    private readonly ILiveUpdateService _liveUpdates;
    private readonly ICachedLookups _lookups;
    private readonly IJobScheduler _jobs;
    private readonly ILogger<PackageCacheService> _logger;

    public PackageCacheService(
        IUnitOfWork unitOfWork,
        IRegistryServiceFactory registryFactory,
        IReleaseNotesRenderer releaseNotesRenderer,
        ILiveUpdateService liveUpdates,
        ICachedLookups lookups,
        IJobScheduler jobs,
        ILogger<PackageCacheService> logger)
    {
        _unitOfWork = unitOfWork;
        _registryFactory = registryFactory;
        _releaseNotesRenderer = releaseNotesRenderer;
        _liveUpdates = liveUpdates;
        _lookups = lookups;
        _jobs = jobs;
        _logger = logger;
    }

    public async Task<CachedPackage?> GetCachedPackageAsync(string packageId, string registry)
    {
        var registryId = await ResolveRegistryIdAsync(registry);
        var cachedPackages = await _unitOfWork.CachedPackages.FindAsync(p => p.PackageId == packageId && p.RegistryId == registryId);
        return cachedPackages.FirstOrDefault();
    }

    public async Task MarkViewedAsync(string registry, string packageId)
    {
        // Tracked, so only this column is written and a scrape saving the same row is not overwritten
        var cached = await GetCachedPackageAsync(packageId, registry.ToLowerInvariant());
        var now = DateTime.UtcNow;
        if (cached is null || cached.LastViewedAt > now.AddHours(-1))
            return;

        cached.LastViewedAt = now;
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task<CachedPackage> GetOrCreateCachedPackageAsync(
        string packageId, string registry, string latestVersion,
        IEnumerable<string> versionStrings)
    {
        var cachedPackage = await GetCachedPackageAsync(packageId, registry);
        if (cachedPackage != null)
            return cachedPackage;

        var client = _registryFactory.GetClient(registry);
        PackageMetadata? pkgMeta = null;
        try
        {
            pkgMeta = await client.GetMetadataAsync(packageId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to fetch metadata for {PackageId}, creating cache without metadata", packageId);
        }

        var registryId = await ResolveRegistryIdAsync(registry);
        cachedPackage = new CachedPackage
        {
            PackageId = packageId,
            RegistryId = registryId,
            LatestVersion = latestVersion,
            Description = pkgMeta?.Description,
            Authors = pkgMeta?.Authors,
            IconUrl = pkgMeta?.IconUrl,
            ProjectUrl = pkgMeta?.ProjectUrl,
            GitHubRepositoryUrl = pkgMeta?.GitHubRepositoryUrl,
            AllVersionsJson = JsonSerializer.Serialize(versionStrings),
            CachedAt = DateTime.UtcNow,
            ExpiresAt = DateTime.UtcNow.AddDays(14)
        };

        await _unitOfWork.CachedPackages.AddAsync(cachedPackage);
        await _unitOfWork.SaveChangesAsync();
        return cachedPackage;
    }

    public async Task UpdateCachedPackageAsync(CachedPackage cachedPackage)
    {
        await _unitOfWork.CachedPackages.UpdateAsync(cachedPackage);
        await _unitOfWork.SaveChangesAsync();
    }

    public async Task<CachedReleaseNote?> GetCachedReleaseNoteAsync(int cachedPackageId, string version)
    {
        var notes = await _unitOfWork.CachedReleaseNotes
            .FindAsync(rn => rn.CachedPackageId == cachedPackageId && rn.Version == version);
        return notes.FirstOrDefault();
    }

    public async Task SaveReleaseNoteAsync(CachedReleaseNote releaseNote)
    {
        if (releaseNote.Id != 0)
        {
            await _unitOfWork.CachedReleaseNotes.UpdateAsync(releaseNote);
            await _unitOfWork.SaveChangesAsync();
            return;
        }

        await _unitOfWork.CachedReleaseNotes.AddAsync(releaseNote);
        try
        {
            await _unitOfWork.SaveChangesAsync();
        }
        catch (DuplicateKeyException)
        {
            // Another request stored this version of the package in the meantime: there is one row per version
            var existing = await GetCachedReleaseNoteAsync(releaseNote.CachedPackageId, releaseNote.Version);
            if (existing == null)
                throw;

            releaseNote.Id = existing.Id;

            // An empty placeholder never replaces notes that were already found
            if (releaseNote.IsEmpty && !existing.IsEmpty)
                return;

            existing.ReleaseNotes = releaseNote.ReleaseNotes;
            existing.ParsedNotesJson = releaseNote.ParsedNotesJson;
            existing.ReleaseNotesSource = releaseNote.ReleaseNotesSource;
            existing.PublishedDate = releaseNote.PublishedDate ?? existing.PublishedDate;
            existing.IsEmpty = releaseNote.IsEmpty;
            existing.CachedAt = releaseNote.CachedAt;
            existing.ExpiresAt = releaseNote.ExpiresAt;
            await _unitOfWork.CachedReleaseNotes.UpdateAsync(existing);
            await _unitOfWork.SaveChangesAsync();
        }
    }

    public async Task<PackageMetadata> GetOrFetchMetadataAsync(string registry, string packageId, bool bypassCache = false)
    {
        var registryLower = registry.ToLowerInvariant();
        var client = _registryFactory.GetClient(registry);

        var cached = await GetCachedPackageAsync(packageId, registryLower);

        // A package whose notes can only come from its repository is not complete until the repository is known
        bool hasValidCacheData = cached != null
            && (client.PublishesReleaseNotes || !string.IsNullOrWhiteSpace(cached.GitHubRepositoryUrl));

        if (hasValidCacheData && !bypassCache)
        {
            if (cached!.ExpiresAt <= DateTime.UtcNow)
            {
                _jobs.Enqueue<IPackageCacheService>(service => service.GetOrFetchMetadataAsync(registryLower, packageId, true));
            }
            return await BuildMetadataFromCache(cached!, registryLower, client);
        }

        var metadata = await client.GetMetadataAsync(packageId);
        metadata.Registry = registryLower;

        await SaveFreshMetadataToCache(cached, metadata, registry, packageId);
        return metadata;
    }

    public Task<int> ResolveRegistryIdAsync(string registryValue)
    {
        return _lookups.GetRegistryIdAsync(registryValue);
    }

    private async Task<PackageMetadata> BuildMetadataFromCache(CachedPackage cached, string registryLower, IRegistryClient registry)
    {
        var cachedNotes = await _unitOfWork.CachedReleaseNotes
            .FindAsync(rn => rn.CachedPackageId == cached.Id);
        var notesLookup = cachedNotes
            .GroupBy(rn => rn.Version)
            .ToDictionary(g => g.Key, g => g.OrderByDescending(rn => rn.CachedAt).First());

        var versions = JsonSerializer.Deserialize<List<string>>(cached.AllVersionsJson) ?? new();

        return new PackageMetadata
        {
            PackageId = cached.PackageId,
            Registry = registryLower,
            LatestVersion = cached.LatestVersion,
            Description = cached.Description,
            Authors = cached.Authors,
            IconUrl = cached.IconUrl,
            ProjectUrl = cached.ProjectUrl,
            GitHubRepositoryUrl = cached.GitHubRepositoryUrl,
            Versions = versions
                .Select(v =>
                {
                    notesLookup.TryGetValue(v, out var note);
                    var entries = !string.IsNullOrWhiteSpace(note?.ParsedNotesJson)
                        ? JsonSerializer.Deserialize<List<ReleaseNoteEntry>>(note.ParsedNotesJson) ?? []
                        : [];
                    entries.RemoveAll(e => CompiledReleaseNotes.IsNotice(e.Text));
                    foreach (var entry in entries)
                        entry.TextHtml = _releaseNotesRenderer.RenderHtml(entry.Text);

                    var releaseNotes = note?.ReleaseNotes ?? string.Empty;
                    var (body, compiledFrom) = CompiledReleaseNotes.Split(releaseNotes);

                    bool isPrerelease = registry.TryParseVersion(v, out var parsed) && parsed.IsPrerelease;

                    return new VersionSummary
                    {
                        Version = v,
                        PublishedDate = note?.PublishedDate,
                        IsPrerelease = isPrerelease,
                        ReleaseNotes = releaseNotes,
                        ReleaseNotesHtml = _releaseNotesRenderer.RenderHtml(body),
                        ReleaseNotesSource = note?.ReleaseNotesSource,
                        CompiledFrom = compiledFrom,
                        Entries = entries,
                        IsEmpty = note?.IsEmpty ?? true,
                    };
                })
                .ToList()
        };
    }

    private async Task SaveFreshMetadataToCache(
        CachedPackage? cached, PackageMetadata metadata, string registry, string packageId)
    {
        var registryId = await ResolveRegistryIdAsync(registry);

        if (cached == null)
        {
            cached = new CachedPackage
            {
                PackageId = packageId,
                RegistryId = registryId,
                LatestVersion = metadata.LatestVersion ?? string.Empty,
                Description = metadata.Description,
                Authors = metadata.Authors,
                IconUrl = metadata.IconUrl,
                ProjectUrl = metadata.ProjectUrl,
                GitHubRepositoryUrl = metadata.GitHubRepositoryUrl,
                AllVersionsJson = JsonSerializer.Serialize(metadata.Versions.Select(v => v.Version)),
                CachedAt = DateTime.UtcNow,
                ExpiresAt = DateTime.UtcNow.AddDays(7)
            };
            await _unitOfWork.CachedPackages.AddAsync(cached);
        }
        else
        {
            cached.RegistryId = registryId;
            cached.Description = metadata.Description;
            cached.Authors = metadata.Authors;
            cached.IconUrl = metadata.IconUrl;
            cached.ProjectUrl = metadata.ProjectUrl;
            cached.GitHubRepositoryUrl = metadata.GitHubRepositoryUrl;
            cached.AllVersionsJson = JsonSerializer.Serialize(metadata.Versions.Select(v => v.Version));
            cached.CachedAt = DateTime.UtcNow;
            cached.ExpiresAt = DateTime.UtcNow.AddDays(14);
            await _unitOfWork.CachedPackages.UpdateAsync(cached);
        }

        await _unitOfWork.SaveChangesAsync();

        var latestVersion = metadata.LatestVersion ?? string.Empty;
        var latestPublished = metadata.Versions.FirstOrDefault(v => v.Version == latestVersion)?.PublishedDate;
        await _liveUpdates.RecordLatestVersionAsync(cached, registry, latestVersion, latestPublished, authoritative: true);

        // A concurrent request may seed some versions first; the missing rows are added in a second pass
        for (var attempt = 0; ; attempt++)
        {
            var existingNotes = (await _unitOfWork.CachedReleaseNotes
                .FindAsync(rn => rn.CachedPackageId == cached.Id))
                .Select(rn => rn.Version)
                .ToHashSet();

            foreach (var v in metadata.Versions)
            {
                if (!existingNotes.Add(v.Version))
                    continue;

                await _unitOfWork.CachedReleaseNotes.AddAsync(new CachedReleaseNote
                {
                    CachedPackageId = cached.Id,
                    Version = v.Version,
                    ReleaseNotes = string.Empty,
                    PublishedDate = v.PublishedDate,
                    IsEmpty = true,
                    CachedAt = DateTime.UtcNow,
                    ExpiresAt = DateTime.UtcNow.AddDays(90)
                });
            }

            try
            {
                await _unitOfWork.SaveChangesAsync();
                return;
            }
            catch (DuplicateKeyException) when (attempt == 0)
            {
            }
        }
    }
}
