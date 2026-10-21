using System.Text.RegularExpressions;
using PkgDiff.Shared.Constants;

namespace PkgDiff.Shared.Validation;

public static class PackageIdRules
{
    public const int MaxPackageIdLength = 214;
    public const int MaxVersionLength = 128;
    public const int MaxQueryLength = 214;

    private static readonly TimeSpan MatchTimeout = TimeSpan.FromMilliseconds(100);

    // \z instead of $ so a trailing newline can never slip through the anchor
    private static readonly Regex NpmId = new(
        @"^(?:@[a-z0-9][a-z0-9._~-]*/)?[A-Za-z0-9][A-Za-z0-9._~-]*\z", RegexOptions.CultureInvariant, MatchTimeout);

    private static readonly Regex NuGetId = new(
        @"^[A-Za-z0-9_]+(?:[.-][A-Za-z0-9_]+)*\z", RegexOptions.CultureInvariant, MatchTimeout);

    private static readonly Regex PyPIId = new(
        @"^[A-Za-z0-9](?:[A-Za-z0-9._-]*[A-Za-z0-9])?\z", RegexOptions.CultureInvariant, MatchTimeout);

    private static readonly Regex PubId = new(
        @"^[a-z0-9_]+\z", RegexOptions.CultureInvariant, MatchTimeout);

    private static readonly Regex PackagistId = new(
        @"^[a-z0-9]+(?:[_.-][a-z0-9]+)*/[a-z0-9]+(?:(?:[_.]|--?)[a-z0-9]+)*\z", RegexOptions.CultureInvariant, MatchTimeout);

    // groupId:artifactId; the group's dots become folders, so none of its parts may be empty
    private static readonly Regex MavenId = new(
        @"^[A-Za-z0-9_-]+(?:\.[A-Za-z0-9_-]+)*:[A-Za-z0-9_][A-Za-z0-9_.-]*\z", RegexOptions.CultureInvariant, MatchTimeout);

    private static readonly IReadOnlyDictionary<string, Regex> Grammars = new Dictionary<string, Regex>
    {
        [RegistryNames.Maven] = MavenId,
        [RegistryNames.NuGet] = NuGetId,
        [RegistryNames.Npm] = NpmId,
        [RegistryNames.PyPI] = PyPIId,
        [RegistryNames.PubDev] = PubId,
        [RegistryNames.Packagist] = PackagistId
    };

    private static readonly Regex Version = new(
        @"^[0-9A-Za-z.+!_~-]{0,128}\z", RegexOptions.CultureInvariant, MatchTimeout);

    public static bool IsKnownRegistry(string? registry) => RegistryNames.Canonical(registry) is not null;

    public static bool IsValidPackageId(string? registry, string? packageId)
    {
        if (string.IsNullOrEmpty(packageId) || packageId.Length > MaxPackageIdLength)
            return false;

        return RegistryNames.Canonical(registry) is { } name && SafeIsMatch(Grammars[name], packageId);
    }

    public static bool IsValidVersion(string? version)
    {
        if (string.IsNullOrEmpty(version))
            return true;

        return version.Length <= MaxVersionLength && SafeIsMatch(Version, version);
    }

    private static bool SafeIsMatch(Regex regex, string input)
    {
        try
        {
            return regex.IsMatch(input);
        }
        catch (RegexMatchTimeoutException)
        {
            return false;
        }
    }
}
