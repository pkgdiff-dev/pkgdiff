using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Interfaces;

public interface IAdminJobService
{
    Task<JobsOverview> GetJobsOverviewAsync();
    Task TriggerJobAsync(string jobId);
    Task DeleteJobAsync(string jobId);
}
