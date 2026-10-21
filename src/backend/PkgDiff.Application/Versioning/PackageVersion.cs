using System.Diagnostics.CodeAnalysis;
using NuGet.Versioning;

namespace PkgDiff.Application.Versioning;

public sealed class PackageVersion : IComparable<PackageVersion>, IEquatable<PackageVersion>
{
    // A Pep440Version, ComposerVersion or NuGetVersion; versions of one package always share the kind
    private readonly IComparable _key;

    private PackageVersion(string value, bool isPrerelease, IComparable key)
    {
        Value = value;
        IsPrerelease = isPrerelease;
        _key = key;
    }

    // The spelling the registry is asked with: its own for npm, pub.dev, PyPI and Packagist, NuGetVersion.ToString() for NuGet
    public string Value { get; }

    public bool IsPrerelease { get; }

    public static bool TryParse(VersionScheme scheme, string? value, [NotNullWhen(true)] out PackageVersion? version)
    {
        version = null;
        if (string.IsNullOrWhiteSpace(value))
            return false;

        value = value.Trim();

        if (scheme == VersionScheme.Pep440)
        {
            if (!Pep440Version.TryParse(value, out var pep440))
                return false;

            version = new PackageVersion(value, pep440.IsPrerelease, pep440);
            return true;
        }

        if (scheme == VersionScheme.Composer)
        {
            if (!ComposerVersion.TryParse(value, out var composer))
                return false;

            version = new PackageVersion(value, composer.IsPrerelease, composer);
            return true;
        }

        if (!NuGetVersion.TryParse(value, out var semantic))
            return false;

        version = new PackageVersion(scheme == VersionScheme.NuGet ? semantic.ToString() : value, semantic.IsPrerelease, semantic);
        return true;
    }

    public static PackageVersion FromNuGet(NuGetVersion version) =>
        new(version.ToString(), version.IsPrerelease, version);

    public NuGetVersion? AsNuGetVersion() => _key as NuGetVersion;

    public int CompareTo(PackageVersion? other) => other is null ? 1 : _key.CompareTo(other._key);

    public bool Equals(PackageVersion? other) => other is not null && _key.CompareTo(other._key) == 0;

    public override bool Equals(object? obj) => Equals(obj as PackageVersion);

    // Equal versions can be spelled differently (1.0 and 1.0.0), so the spelling cannot feed the hash
    public override int GetHashCode() => _key is NuGetVersion semantic ? semantic.GetHashCode() : 0;

    public override string ToString() => Value;

    public static bool operator <(PackageVersion left, PackageVersion right) => left.CompareTo(right) < 0;

    public static bool operator >(PackageVersion left, PackageVersion right) => left.CompareTo(right) > 0;

    public static bool operator <=(PackageVersion left, PackageVersion right) => left.CompareTo(right) <= 0;

    public static bool operator >=(PackageVersion left, PackageVersion right) => left.CompareTo(right) >= 0;
}
