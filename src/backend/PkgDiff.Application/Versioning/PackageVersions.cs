namespace PkgDiff.Application.Versioning;

public static class PackageVersions
{
    // Ascending by the scheme's ordering; what the registry lists but cannot be parsed ends up in skipped
    public static List<PackageVersion> ParseAll(
        VersionScheme scheme, IEnumerable<string> values, bool includePrerelease, out List<string> skipped)
    {
        var versions = new List<PackageVersion>();
        skipped = new List<string>();

        foreach (var value in values)
        {
            if (!PackageVersion.TryParse(scheme, value, out var version))
                skipped.Add(value);
            else if (includePrerelease || !version.IsPrerelease)
                versions.Add(version);
        }

        versions.Sort();
        return versions;
    }
}
