using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Security;

// What OSV knows about a set of versions of one package
public sealed record OsvReport(
    Dictionary<string, List<string>> VersionAdvisories,
    List<SecurityAdvisory> Advisories);
