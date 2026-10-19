using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Jobs;
using PkgDiff.Shared.DTOs;
using Hangfire;
using Hangfire.Storage;
using Hangfire.Storage.Monitoring;

namespace PkgDiff.Application.Services;

public class AdminJobService : IAdminJobService
{
    public Task<JobsOverview> GetJobsOverviewAsync()
    {
        var monitor = JobStorage.Current.GetMonitoringApi();
        var stats = monitor.GetStatistics();

        using var connection = JobStorage.Current.GetConnection();
        var recurringJobs = connection.GetRecurringJobs();

        var recurringJobInfos = recurringJobs.Select(j => new RecurringJobInfo
        {
            Id = j.Id,
            Cron = j.Cron,
            LastExecution = j.LastExecution,
            NextExecution = j.NextExecution,
            LastJobState = j.LastJobState ?? "None"
        }).ToList();

        var succeeded = monitor.SucceededJobs(0, 50);
        var failed = monitor.FailedJobs(0, 50);

        var allJobs = new List<RecentJobInfo>();

        foreach (var job in succeeded)
        {
            var details = monitor.JobDetails(job.Key);
            var target = JobDescriptions.Target(job.Value.Job);
            allJobs.Add(new RecentJobInfo
            {
                JobId = job.Key,
                JobName = JobDescriptions.TechnicalName(job.Value.Job),
                JobType = JobDescriptions.Label(job.Value.Job),
                State = "Succeeded",
                CreatedAt = details?.CreatedAt,
                CompletedAt = job.Value.SucceededAt,
                Duration = job.Value.TotalDuration,
                PackageId = target.PackageId,
                Registry = target.Registry,
                Detail = target.Detail,
                TriggeredBy = DescribeTrigger(details)
            });
        }

        foreach (var job in failed)
        {
            var details = monitor.JobDetails(job.Key);
            var target = JobDescriptions.Target(job.Value.Job);
            allJobs.Add(new RecentJobInfo
            {
                JobId = job.Key,
                JobName = JobDescriptions.TechnicalName(job.Value.Job),
                JobType = JobDescriptions.Label(job.Value.Job),
                State = "Failed",
                CreatedAt = details?.CreatedAt,
                CompletedAt = job.Value.FailedAt,
                ExceptionMessage = job.Value.ExceptionMessage,
                PackageId = target.PackageId,
                Registry = target.Registry,
                Detail = target.Detail,
                TriggeredBy = DescribeTrigger(details)
            });
        }

        var parentJobs = allJobs
            .Where(j => j.JobName.Contains("ExecuteAsync"))
            .OrderByDescending(j => j.CreatedAt)
            .ToList();

        var childPool = allJobs
            .Where(j => !j.JobName.Contains("ExecuteAsync"))
            .ToList();

        var recentJobs = new List<RecentJobInfo>();

        foreach (var parent in parentJobs)
        {
            var children = childPool
                .Where(c => parent.CreatedAt.HasValue && c.CreatedAt.HasValue &&
                            Math.Abs((c.CreatedAt.Value - parent.CreatedAt.Value).TotalSeconds) < 5)
                .OrderBy(c => c.PackageId)
                .ToList();

            parent.ChildJobs = children;

            foreach (var child in children)
                childPool.Remove(child);

            recentJobs.Add(parent);
        }

        recentJobs.AddRange(childPool);

        recentJobs = recentJobs
            .OrderByDescending(j => j.CompletedAt ?? j.CreatedAt)
            .Take(20)
            .ToList();

        var queuedJobs = new List<QueuedJobInfo>();

        var queues = monitor.Queues();
        foreach (var queue in queues)
        {
            var enqueuedJobs = monitor.EnqueuedJobs(queue.Name, 0, 50);
            foreach (var job in enqueuedJobs)
            {
                var target = JobDescriptions.Target(job.Value.Job);
                queuedJobs.Add(new QueuedJobInfo
                {
                    JobId = job.Key,
                    JobName = JobDescriptions.TechnicalName(job.Value.Job),
                    JobType = JobDescriptions.Label(job.Value.Job),
                    State = "Enqueued",
                    EnqueuedAt = job.Value.EnqueuedAt,
                    PackageId = target.PackageId,
                    Registry = target.Registry,
                    Detail = target.Detail,
                    TriggeredBy = DescribeTrigger(monitor.JobDetails(job.Key))
                });
            }
        }

        var processingJobs = monitor.ProcessingJobs(0, 50);
        foreach (var job in processingJobs)
        {
            var target = JobDescriptions.Target(job.Value.Job);
            queuedJobs.Add(new QueuedJobInfo
            {
                JobId = job.Key,
                JobName = JobDescriptions.TechnicalName(job.Value.Job),
                JobType = JobDescriptions.Label(job.Value.Job),
                State = "Processing",
                StartedAt = job.Value.StartedAt,
                ServerId = job.Value.ServerId,
                PackageId = target.PackageId,
                Registry = target.Registry,
                Detail = target.Detail,
                TriggeredBy = DescribeTrigger(monitor.JobDetails(job.Key))
            });
        }

        return Task.FromResult(new JobsOverview
        {
            Enqueued = stats.Enqueued,
            Processing = stats.Processing,
            Succeeded = stats.Succeeded,
            Failed = stats.Failed,
            Scheduled = stats.Scheduled,
            RecurringJobs = recurringJobInfos,
            RecentJobs = recentJobs,
            QueuedJobs = queuedJobs
        });
    }

    private static string? DescribeTrigger(JobDetailsDto? details)
    {
        if (details?.Properties == null) return null;

        if (details.Properties.TryGetValue(JobDescriptions.TriggerParameter, out var trigger))
            return ReadParameter(trigger);

        if (details.Properties.TryGetValue("RecurringJobId", out var recurringJobId))
            return $"Schedule · {ReadParameter(recurringJobId)}";

        return null;
    }

    private static string? ReadParameter(string? value)
    {
        if (string.IsNullOrEmpty(value)) return null;

        try
        {
            return System.Text.Json.JsonSerializer.Deserialize<string>(value);
        }
        catch (System.Text.Json.JsonException)
        {
            return value.Trim('"');
        }
    }

    public Task TriggerJobAsync(string jobId)
    {
        RecurringJob.TriggerJob(jobId);
        return Task.CompletedTask;
    }

    public Task DeleteJobAsync(string jobId)
    {
        BackgroundJob.Delete(jobId);
        return Task.CompletedTask;
    }
}
