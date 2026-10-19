using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Services;

public static class WorkKeys
{
    public const string AnalysisPrefix = "analysis|";

    // Two requests share one run only when registry, package, both versions and the prerelease flag are the same
    public static string Analysis(AnalyzeRequest request) => AnalysisPrefix + Range(request);

    // Background refreshes have their own prefix, so they do not use up the slots of visitors' analyses
    public static string Refresh(AnalyzeRequest request) => "refresh|" + Range(request);

    public static string Scrape(string registry, string packageId) =>
        $"scrape|{registry.ToLowerInvariant()}|{packageId}";

    public static string Security(string registry, string packageId) =>
        $"security|{registry.ToLowerInvariant()}|{packageId.ToLowerInvariant()}";

    private static string Range(AnalyzeRequest request) =>
        $"{request.Registry.ToLowerInvariant()}|{request.PackageId.ToLowerInvariant()}|{request.CurrentVersion}|{request.TargetVersion}|{request.IncludePrerelease}";
}
