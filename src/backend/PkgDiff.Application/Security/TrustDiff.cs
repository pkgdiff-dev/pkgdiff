using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Security;

// What an upgrade fixes (in FROM, gone in TO) and what it introduces (in TO, not in FROM)
public static class TrustDiff
{
    public static IReadOnlyList<SecurityAdvisory> AdvisoriesOf(PackageTrust trust, string version)
    {
        if (!trust.VersionAdvisories.TryGetValue(version, out var ids))
            return Array.Empty<SecurityAdvisory>();
        var byId = trust.Advisories
            .GroupBy(a => a.Id, StringComparer.Ordinal)
            .ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
        return ids.Select(id => byId.TryGetValue(id, out var advisory) ? advisory : new SecurityAdvisory { Id = id }).ToList();
    }

    public static IReadOnlyList<SecurityAdvisory> Fixed(PackageTrust trust, string from, string to)
    {
        var after = AdvisoriesOf(trust, to).Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        return AdvisoriesOf(trust, from).Where(a => !after.Contains(a.Id)).ToList();
    }

    public static IReadOnlyList<SecurityAdvisory> Introduced(PackageTrust trust, string from, string to)
    {
        var before = AdvisoriesOf(trust, from).Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        return AdvisoriesOf(trust, to).Where(a => !before.Contains(a.Id)).ToList();
    }

    // Versions among these that a malicious-package record names
    public static IReadOnlyList<string> MaliciousVersions(PackageTrust trust, IEnumerable<string> versions)
    {
        var malicious = trust.Advisories.Where(a => a.IsMalicious).Select(a => a.Id).ToHashSet(StringComparer.Ordinal);
        if (malicious.Count == 0)
            return Array.Empty<string>();
        return versions
            .Where(v => trust.VersionAdvisories.TryGetValue(v, out var ids) && ids.Any(malicious.Contains))
            .ToList();
    }
}
