using System.Net;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Versioning;
using PkgDiff.Domain.Entities;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Shared.Constants;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Services;

// Audits run in a background job and are stored; nothing is fetched while the visitor waits
public class DependencyAuditService : IDependencyAuditService
{
    private static readonly TimeSpan Lifetime = TimeSpan.FromDays(30);
    // A job that has not finished within this time is no longer counted as running
    private static readonly TimeSpan StaleAfter = TimeSpan.FromHours(1);
    private const int ProgressEvery = 5;

    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    private readonly IUnitOfWork _unitOfWork;
    private readonly IRegistryServiceFactory _registries;
    private readonly IOsvClient _osv;
    private readonly IJobScheduler _jobs;
    private readonly ILogger<DependencyAuditService> _logger;

    public DependencyAuditService(
        IUnitOfWork unitOfWork,
        IRegistryServiceFactory registries,
        IOsvClient osv,
        IJobScheduler jobs,
        ILogger<DependencyAuditService> logger)
    {
        _unitOfWork = unitOfWork;
        _registries = registries;
        _osv = osv;
        _jobs = jobs;
        _logger = logger;
    }

    public async Task<Guid> StartAsync(StartAuditRequest request)
    {
        var registry = RegistryNames.Canonical(request.Registry) ?? throw new ArgumentException("Unknown registry.");
        var dependencies = request.Dependencies
            .Where(d => !string.IsNullOrWhiteSpace(d.Name))
            .GroupBy(d => d.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .Select(g => g.First())
            .Take(AuditLimits.MaxDependencies)
            .Select(d => new AuditDependency
            {
                Name = d.Name.Trim(),
                Version = string.IsNullOrWhiteSpace(d.Version) ? null : d.Version.Trim(),
                Requirement = d.Requirement?.Trim(),
                IsExact = d.IsExact,
                Scope = d.Scope
            })
            .ToList();

        var now = DateTime.UtcNow;
        var audit = new DependencyAudit
        {
            PublicId = Guid.NewGuid(),
            Registry = registry,
            Format = request.Format,
            Status = AuditStatus.Queued,
            DependenciesJson = JsonSerializer.Serialize(dependencies, Json),
            Total = dependencies.Count,
            CreatedAt = now,
            ExpiresAt = now + Lifetime
        };
        await _unitOfWork.DependencyAudits.AddAsync(audit);
        await _unitOfWork.SaveChangesAsync();

        var id = audit.PublicId;
        _jobs.Enqueue<IDependencyAuditService>(service => service.RunAsync(id));
        return id;
    }

    public async Task<AuditReport?> GetAsync(Guid id)
    {
        var audit = await FindAsync(id);
        if (audit is null || audit.ExpiresAt <= DateTime.UtcNow)
            return null;

        var report = new AuditReport
        {
            Id = audit.PublicId,
            Registry = audit.Registry,
            Format = audit.Format,
            Status = audit.Status,
            Progress = audit.Progress,
            Total = audit.Total,
            CreatedAt = audit.CreatedAt,
            CompletedAt = audit.CompletedAt,
            ExpiresAt = audit.ExpiresAt,
            Error = audit.Error
        };
        if (audit.ReportJson is not null)
        {
            var stored = JsonSerializer.Deserialize<StoredReport>(audit.ReportJson, Json);
            report.Summary = stored?.Summary ?? new AuditSummary();
            report.Items = stored?.Items ?? new List<AuditItem>();
        }
        return report;
    }

    public async Task<int> CountActiveAsync()
    {
        var since = DateTime.UtcNow - StaleAfter;
        return await _unitOfWork.DependencyAudits.CountAsync(a =>
            (a.Status == AuditStatus.Queued || a.Status == AuditStatus.Running) && a.CreatedAt > since);
    }

    public async Task RunAsync(Guid id)
    {
        var audit = await FindAsync(id);
        if (audit is null || audit.Status == AuditStatus.Completed)
            return;

        audit.Status = AuditStatus.Running;
        audit.Progress = 0;
        await SaveAsync(audit);

        try
        {
            var dependencies = JsonSerializer.Deserialize<List<AuditDependency>>(audit.DependenciesJson, Json) ?? new();
            var client = _registries.GetClient(audit.Registry);
            var items = new List<AuditItem>(dependencies.Count);

            // One package after the other: the registry clients share the job's database context
            foreach (var dependency in dependencies)
            {
                items.Add(await LookUpAsync(client, dependency));
                if (items.Count % ProgressEvery == 0)
                {
                    audit.Progress = items.Count;
                    await SaveAsync(audit);
                }
            }

            var checkable = items.Where(i => i.Status == AuditItemStatus.Clean && i.Version is not null).ToList();
            var results = await _osv.QueryPackagesAsync(audit.Registry, checkable.Select(i => (i.Name, i.Version!)).ToList());
            for (var i = 0; i < checkable.Count; i++)
            {
                var item = checkable[i];
                item.Advisories = results[i].Advisories;
                if (item.Advisories.Count == 0)
                    continue;
                item.Status = AuditItemStatus.Vulnerable;
                item.IsMalicious = item.Advisories.Any(a => a.IsMalicious);
                (item.SafeVersion, item.HasUnfixedAdvisory) = SafeVersionOf(client.VersionScheme, item.Version!, item.Advisories);
            }

            audit.ReportJson = JsonSerializer.Serialize(new StoredReport(Summarize(items), items), Json);
            audit.Status = AuditStatus.Completed;
            audit.Progress = audit.Total;
            audit.CompletedAt = DateTime.UtcNow;
            await SaveAsync(audit);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Dependency audit {AuditId} failed", id);
            audit.Status = AuditStatus.Failed;
            audit.Error = "The audit could not be completed. Please try again later.";
            await SaveAsync(audit);
        }
    }

    public async Task DeleteExpiredAsync()
    {
        var now = DateTime.UtcNow;
        var deleted = await _unitOfWork.DependencyAudits.DeleteWhereAsync(a => a.ExpiresAt <= now);
        if (deleted > 0)
            _logger.LogInformation("Deleted {Count} expired dependency audits", deleted);
    }

    private async Task<AuditItem> LookUpAsync(IRegistryClient client, AuditDependency dependency)
    {
        var item = new AuditItem
        {
            Name = dependency.Name,
            Requirement = dependency.Requirement,
            Scope = dependency.Scope,
            IsExact = dependency.IsExact
        };

        try
        {
            var versions = await client.GetVersionsAsync(dependency.Name, includePrerelease: false);
            if (versions.Count == 0)
                versions = await client.GetVersionsAsync(dependency.Name, includePrerelease: true);
            if (versions.Count == 0)
            {
                item.Status = AuditItemStatus.NotFound;
                return item;
            }

            item.LatestVersion = versions[^1].Value;
            if (dependency.Version is null)
            {
                item.Version = item.LatestVersion;
                item.VersionAssumed = true;
            }
            else
            {
                // The registry's own spelling of the same version
                item.Version = PackageVersion.TryParse(client.VersionScheme, dependency.Version, out var parsed)
                    ? versions.FirstOrDefault(v => v.Equals(parsed))?.Value ?? dependency.Version
                    : dependency.Version;
            }
            item.Status = AuditItemStatus.Clean;
        }
        catch (Exception ex) when (ex is ArgumentException or KeyNotFoundException
                                       || ex is HttpRequestException { StatusCode: HttpStatusCode.NotFound })
        {
            item.Status = AuditItemStatus.NotFound;
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            _logger.LogWarning(ex, "Audit lookup of {PackageId} failed", dependency.Name);
            item.Status = AuditItemStatus.Unchecked;
            item.Version = dependency.Version;
        }
        return item;
    }

    // Lowest version fixing every advisory: the highest first-fixed version above the checked one
    private static (string? Safe, bool HasUnfixed) SafeVersionOf(VersionScheme scheme, string current, List<SecurityAdvisory> advisories)
    {
        if (!PackageVersion.TryParse(scheme, current, out var checkedVersion))
            return (null, false);

        PackageVersion? safe = null;
        var hasUnfixed = false;
        foreach (var advisory in advisories)
        {
            var firstFix = advisory.FixedIn
                .Select(v => PackageVersion.TryParse(scheme, v, out var parsed) ? parsed : null)
                .Where(v => v is not null && v > checkedVersion)
                .OrderBy(v => v)
                .FirstOrDefault();
            if (firstFix is null)
            {
                hasUnfixed = true;
                continue;
            }
            if (safe is null || firstFix > safe)
                safe = firstFix;
        }
        return (safe?.Value, hasUnfixed);
    }

    private static AuditSummary Summarize(List<AuditItem> items)
    {
        var summary = new AuditSummary
        {
            Packages = items.Count,
            Vulnerable = items.Count(i => i.Status == AuditItemStatus.Vulnerable),
            Malicious = items.Count(i => i.IsMalicious),
            NotFound = items.Count(i => i.Status == AuditItemStatus.NotFound)
        };
        foreach (var advisory in items.SelectMany(i => i.Advisories))
        {
            switch (advisory.Severity)
            {
                case AdvisorySeverity.Critical: summary.Critical++; break;
                case AdvisorySeverity.High: summary.High++; break;
                case AdvisorySeverity.Medium: summary.Medium++; break;
                case AdvisorySeverity.Low: summary.Low++; break;
                default: summary.UnknownSeverity++; break;
            }
        }
        return summary;
    }

    private async Task<DependencyAudit?> FindAsync(Guid id) =>
        (await _unitOfWork.DependencyAudits.FindAsync(a => a.PublicId == id)).FirstOrDefault();

    private async Task SaveAsync(DependencyAudit audit)
    {
        await _unitOfWork.DependencyAudits.UpdateAsync(audit);
        await _unitOfWork.SaveChangesAsync();
    }

    private sealed record StoredReport(AuditSummary Summary, List<AuditItem> Items);
}
