using System.Text.RegularExpressions;
using Hangfire.Common;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Jobs;

public static class JobDescriptions
{
    public const string TriggerParameter = "TriggeredBy";

    private static readonly Dictionary<string, string> Labels = new()
    {
        ["PackageAnalysisJob.ExecuteAsync"] = "Cache refresh sweep",
        ["PackageRefreshJob.RefreshAsync"] = "Package cache refresh",
        ["IPackageTrustService.RefreshAsync"] = "Security scan",
        ["IPackageTrustService.RefreshExpiredAsync"] = "Expired security re-scan",
        ["IPackageCacheService.GetOrFetchMetadataAsync"] = "Metadata refresh",
        ["IPackageAnalysisService.ScrapePackageReleaseNotesAsync"] = "Release notes scrape",
        ["IDependencyAuditService.RunAsync"] = "Dependency audit",
        ["IDependencyAuditService.DeleteExpiredAsync"] = "Audit cleanup",
        ["DatabaseCleanupJob.CleanupOldRequestLogsAsync"] = "Request log cleanup",
        ["IAutoPilotService.RunAutoPilotJobAsync"] = "AutoPilot blog generation",
        ["BlogSocialPostJob.ExecuteAsync"] = "Blog social post",
        ["IContributorService.BackfillFromExistingReleaseNotesAsync"] = "Contributor backfill"
    };

    public static string TechnicalName(Job? job) =>
        job == null ? "Unknown" : $"{job.Type.Name}.{job.Method.Name}";

    public static string Label(Job? job)
    {
        if (job == null) return "Unknown job";
        return Labels.TryGetValue(TechnicalName(job), out var label) ? label : Humanize(job.Method.Name);
    }

    public static JobTarget Target(Job? job)
    {
        var target = new JobTarget();
        if (job?.Args == null) return target;

        var parameters = job.Method.GetParameters();
        var details = new List<string>();

        for (var i = 0; i < parameters.Length && i < job.Args.Count; i++)
        {
            var arg = job.Args[i];
            switch (arg)
            {
                case null or bool:
                    break;
                case AnalyzeRequest request:
                    target.Registry = request.Registry;
                    target.PackageId = request.PackageId;
                    if (!string.IsNullOrEmpty(request.CurrentVersion) || !string.IsNullOrEmpty(request.TargetVersion))
                        details.Add($"{request.CurrentVersion} → {request.TargetVersion}");
                    break;
                case Guid id:
                    details.Add(id.ToString("N")[..8]);
                    break;
                default:
                    var value = arg.ToString();
                    if (string.IsNullOrWhiteSpace(value)) break;
                    if (parameters[i].Name == "registry") target.Registry = value;
                    else if (parameters[i].Name == "packageId") target.PackageId = value;
                    else details.Add($"{Humanize(parameters[i].Name ?? string.Empty)} {value}");
                    break;
            }
        }

        target.Detail = details.Count > 0 ? string.Join(", ", details) : null;
        return target;
    }

    private static string Humanize(string name)
    {
        var trimmed = name.EndsWith("Async") ? name[..^5] : name;
        var spaced = Regex.Replace(trimmed, "(?<=[a-z0-9])(?=[A-Z])", " ").ToLowerInvariant();
        return spaced.Length == 0 ? name : char.ToUpperInvariant(spaced[0]) + spaced[1..];
    }
}
