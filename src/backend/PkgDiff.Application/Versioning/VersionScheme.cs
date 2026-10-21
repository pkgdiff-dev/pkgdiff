namespace PkgDiff.Application.Versioning;

public enum VersionScheme
{
    // NuGet's own rules; a version is spelled the way NuGet normalizes it
    NuGet,

    // SemVer in the registry's own spelling: npm and pub.dev
    SemVer,

    // PEP 440: PyPI
    Pep440,

    // Composer's rules: Packagist
    Composer,

    // Maven's ComparableVersion: Maven Central
    Maven
}
