using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Interfaces;

public interface IDependencyAuditService
{
    // Stores the request and queues the background job; returns the id of the report
    Task<Guid> StartAsync(StartAuditRequest request);

    // null when there is no such report or it has expired
    Task<AuditReport?> GetAsync(Guid id);

    // Audits waiting or running; the start endpoint refuses new ones above a limit
    Task<int> CountActiveAsync();

    // Background job, on its own queue: a burst of audits must not hold the workers scrapes need
    [Hangfire.Queue(AuditQueue)]
    Task RunAsync(Guid id);

    const string AuditQueue = "audits";

    // Recurring job
    Task DeleteExpiredAsync();
}
