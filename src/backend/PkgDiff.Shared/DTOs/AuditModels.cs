using System.ComponentModel.DataAnnotations;
using PkgDiff.Shared.Validation;

namespace PkgDiff.Shared.DTOs;

public static class AuditStatus
{
    public const string Queued = "queued";
    public const string Running = "running";
    public const string Completed = "completed";
    public const string Failed = "failed";
}

public static class AuditItemStatus
{
    public const string Vulnerable = "vulnerable";
    public const string Clean = "clean";
    // The registry does not know the package (a private or misspelled one)
    public const string NotFound = "not-found";
    // The version could not be checked (the registry could not be reached)
    public const string Unchecked = "unchecked";
}

public class ManifestParseRequest
{
    [Required]
    public string Registry { get; set; } = string.Empty;

    // The pasted file; 1 MB is far more than any real manifest or lock file of 200 packages needs
    [Required]
    [StringLength(AuditLimits.MaxContentLength)]
    public string Content { get; set; } = string.Empty;

    [StringLength(260)]
    public string? FileName { get; set; }
}

public static class AuditLimits
{
    public const int MaxContentLength = 1_048_576;
    public const int MaxDependencies = 200;
    public const int MaxRequirementLength = 128;
    public const int MaxScopeLength = 32;
}

public class AuditDependency
{
    public string Name { get; set; } = string.Empty;

    // The version that is checked; null means "the latest version the registry has"
    public string? Version { get; set; }

    // What the file says, e.g. "^4.17.1" or ">=2.0,<3"
    public string? Requirement { get; set; }

    // True when the file pins exactly this version (lock files, "==1.2.3", "[1.2.3]")
    public bool IsExact { get; set; }

    // "dev", "optional" or null for a normal dependency
    public string? Scope { get; set; }
}

public class ManifestParseResult
{
    public string Registry { get; set; } = string.Empty;

    // The detected file type, e.g. "package.json" or "requirements.txt"
    public string Format { get; set; } = string.Empty;

    public List<AuditDependency> Dependencies { get; set; } = new();

    public List<string> Warnings { get; set; } = new();
}

public class StartAuditRequest : IValidatableObject
{
    [Required]
    public string Registry { get; set; } = string.Empty;

    [StringLength(64)]
    public string? Format { get; set; }

    public List<AuditDependency> Dependencies { get; set; } = new();

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!PackageIdRules.IsKnownRegistry(Registry))
        {
            yield return new ValidationResult("Unknown registry.", new[] { nameof(Registry) });
            yield break;
        }

        if (Dependencies.Count == 0)
            yield return new ValidationResult("There is no package to check.", new[] { nameof(Dependencies) });

        if (Dependencies.Count > AuditLimits.MaxDependencies)
            yield return new ValidationResult($"At most {AuditLimits.MaxDependencies} packages can be checked at once.", new[] { nameof(Dependencies) });

        foreach (var dependency in Dependencies)
        {
            if (!PackageIdRules.IsValidPackageId(Registry, dependency.Name))
            {
                yield return new ValidationResult($"'{Trim(dependency.Name)}' is not a valid package name.", new[] { nameof(Dependencies) });
                yield break;
            }

            if (dependency.Requirement?.Length > AuditLimits.MaxRequirementLength || dependency.Scope?.Length > AuditLimits.MaxScopeLength)
            {
                yield return new ValidationResult($"The requirement of {Trim(dependency.Name)} is too long.", new[] { nameof(Dependencies) });
                yield break;
            }

            if (!string.IsNullOrEmpty(dependency.Version) && !PackageIdRules.IsValidVersion(dependency.Version))
            {
                yield return new ValidationResult($"'{Trim(dependency.Version)}' is not a valid version of {dependency.Name}.", new[] { nameof(Dependencies) });
                yield break;
            }
        }
    }

    private static string Trim(string? value) =>
        value is null ? string.Empty : value.Length <= 80 ? value : value[..80] + "…";
}

public class StartAuditResponse
{
    public Guid Id { get; set; }
}

public class AuditReport
{
    public Guid Id { get; set; }
    public string Registry { get; set; } = string.Empty;
    public string? Format { get; set; }
    public string Status { get; set; } = AuditStatus.Queued;
    public int Progress { get; set; }
    public int Total { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
    public string? Error { get; set; }
    public AuditSummary Summary { get; set; } = new();
    public List<AuditItem> Items { get; set; } = new();
}

public class AuditSummary
{
    public int Packages { get; set; }
    public int Vulnerable { get; set; }
    public int Malicious { get; set; }
    public int NotFound { get; set; }
    public int Critical { get; set; }
    public int High { get; set; }
    public int Medium { get; set; }
    public int Low { get; set; }
    public int UnknownSeverity { get; set; }
}

public class AuditItem
{
    public string Name { get; set; } = string.Empty;
    public string? Requirement { get; set; }
    public string? Scope { get; set; }
    public bool IsExact { get; set; }

    // The version that was checked
    public string? Version { get; set; }

    // True when the file named no usable version and the latest one was checked instead
    public bool VersionAssumed { get; set; }

    public string Status { get; set; } = AuditItemStatus.Unchecked;
    public string? LatestVersion { get; set; }
    public List<SecurityAdvisory> Advisories { get; set; } = new();

    // The lowest version that fixes every advisory of the checked version; null when one of them
    // has no fixed version yet (or there is nothing to fix)
    public string? SafeVersion { get; set; }

    // True when at least one advisory has no fixed version published
    public bool HasUnfixedAdvisory { get; set; }

    public bool IsMalicious { get; set; }
}
