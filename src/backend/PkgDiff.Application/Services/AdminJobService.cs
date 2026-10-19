using PkgDiff.Application.Interfaces;
using PkgDiff.Domain.Entities;
using PkgDiff.Domain.Interfaces;
using PkgDiff.Shared.DTOs;
using Hangfire;
using Hangfire.Storage;
using Microsoft.AspNetCore.Identity;

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

        // Recent completed jobs — fetch more to capture full batches
        var succeeded = monitor.SucceededJobs(0, 50);
        var failed = monitor.FailedJobs(0, 50);

        var allJobs = new List<RecentJobInfo>();

        foreach (var job in succeeded)
        {
            var details = monitor.JobDetails(job.Key);
            allJobs.Add(new RecentJobInfo
            {
                JobId = job.Key,
                JobName = GetJobDisplayName(job.Value.Job),
                State = "Succeeded",
                CreatedAt = details?.CreatedAt,
                CompletedAt = job.Value.SucceededAt,
                Duration = job.Value.TotalDuration,
                PackageId = ExtractPackageId(job.Value.Job)
            });
        }

        foreach (var job in failed)
        {
            var details = monitor.JobDetails(job.Key);
            allJobs.Add(new RecentJobInfo
            {
                JobId = job.Key,
                JobName = GetJobDisplayName(job.Value.Job),
                State = "Failed",
                CreatedAt = details?.CreatedAt,
                CompletedAt = job.Value.FailedAt,
                ExceptionMessage = job.Value.ExceptionMessage,
                PackageId = ExtractPackageId(job.Value.Job)
            });
        }

        // Group: parent (ExecuteAsync) jobs get children attached
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
            // Match children created within 5 seconds of parent's CreatedAt
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

        // Any unmatched children as standalone entries
        recentJobs.AddRange(childPool);

        recentJobs = recentJobs
            .OrderByDescending(j => j.CompletedAt ?? j.CreatedAt)
            .Take(20)
            .ToList();

        // Active queue jobs (enqueued + processing)
        var queuedJobs = new List<QueuedJobInfo>();

        var queues = monitor.Queues();
        foreach (var queue in queues)
        {
            var enqueuedJobs = monitor.EnqueuedJobs(queue.Name, 0, 50);
            foreach (var job in enqueuedJobs)
            {
                queuedJobs.Add(new QueuedJobInfo
                {
                    JobId = job.Key,
                    JobName = GetJobDisplayName(job.Value.Job),
                    State = "Enqueued",
                    EnqueuedAt = job.Value.EnqueuedAt,
                    PackageId = ExtractPackageId(job.Value.Job)
                });
            }
        }

        var processingJobs = monitor.ProcessingJobs(0, 50);
        foreach (var job in processingJobs)
        {
            queuedJobs.Add(new QueuedJobInfo
            {
                JobId = job.Key,
                JobName = GetJobDisplayName(job.Value.Job),
                State = "Processing",
                StartedAt = job.Value.StartedAt,
                ServerId = job.Value.ServerId,
                PackageId = ExtractPackageId(job.Value.Job)
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

    private static string GetJobDisplayName(Hangfire.Common.Job? job)
    {
        if (job == null) return "Unknown";
        return $"{job.Type.Name}.{job.Method.Name}";
    }

    private static string? ExtractPackageId(Hangfire.Common.Job? job)
    {
        if (job?.Args == null || job.Args.Count == 0) return null;

        // Try direct cast (Hangfire deserializes to the actual type)
        if (job.Args[0] is AnalyzeRequest request)
            return request.PackageId;

        // Fallback: parse from serialized JSON string
        try
        {
            var json = job.Args[0]?.ToString();
            if (json != null && json.Contains("PackageId"))
            {
                using var doc = System.Text.Json.JsonDocument.Parse(json);
                if (doc.RootElement.TryGetProperty("PackageId", out var prop))
                    return prop.GetString();
            }
        }
        catch (System.Text.Json.JsonException)
        {
            // The argument is not JSON: this job has no package to show
        }

        return null;
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
