using System.Diagnostics.CodeAnalysis;
using PkgDiff.Application.Interfaces;
using PkgDiff.Application.Versioning;

namespace PkgDiff.Application.Services;

public static class RegistryClientExtensions
{
    public static bool TryParseVersion(
        this IRegistryClient registry, string? value, [NotNullWhen(true)] out PackageVersion? version) =>
        PackageVersion.TryParse(registry.VersionScheme, value, out version);

    public static async Task<List<PackageVersion>> GetVersionsInRangeAsync(
        this IRegistryClient registry, string packageId, PackageVersion from, PackageVersion to, bool includePrerelease)
    {
        var versions = await registry.GetVersionsAsync(packageId, includePrerelease);
        var includeFrom = registry.RangeIncludesStartingVersion;

        // Newest first, so early stopping never skips the recent notes
        return versions
            .Where(v => (includeFrom ? v >= from : v > from) && v <= to)
            .OrderByDescending(v => v)
            .ToList();
    }
}
