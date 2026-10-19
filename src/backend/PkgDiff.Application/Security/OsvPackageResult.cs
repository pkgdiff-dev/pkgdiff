using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Security;

// What OSV knows about one version of one package, with the fixed versions of that package
public sealed record OsvPackageResult(string Name, string Version, List<SecurityAdvisory> Advisories);
