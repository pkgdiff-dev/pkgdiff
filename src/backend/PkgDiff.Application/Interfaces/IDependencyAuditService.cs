using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Interfaces;

public interface IDependencyAuditService
{
    Task<Guid> StartAsync(StartAuditRequest request);

    Task<AuditReport?> GetAsync(Guid id);

    Task<int> CountActiveAsync();

    // Own queue: a burst of audits must not hold the workers scrapes need
    [Hangfire.Queue(AuditQueue)]
    Task RunAsync(Guid id);

    const string AuditQueue = "audits";

    Task DeleteExpiredAsync();
}
