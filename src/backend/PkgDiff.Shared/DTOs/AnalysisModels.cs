using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;
using PkgDiff.Shared.Validation;

namespace PkgDiff.Shared.DTOs;

public class AnalyzeRequest : IValidatableObject
{
    [Required]
    [StringLength(PackageIdRules.MaxPackageIdLength)]
    public string PackageId { get; set; } = string.Empty;

    [Required]
    public string Registry { get; set; } = "nuget";

    [StringLength(PackageIdRules.MaxVersionLength)]
    public string CurrentVersion { get; set; } = string.Empty;

    [StringLength(PackageIdRules.MaxVersionLength)]
    public string TargetVersion { get; set; } = string.Empty;

    public bool IncludePrerelease { get; set; } = false;

    [StringLength(128)]
    public string? ConnectionId { get; set; }

    [StringLength(1024)]
    public string? ProgressToken { get; set; }

    public IEnumerable<ValidationResult> Validate(ValidationContext validationContext)
    {
        if (!PackageIdRules.IsKnownRegistry(Registry))
        {
            yield return new ValidationResult("Unknown registry.", new[] { nameof(Registry) });
        }
        else if (!string.IsNullOrEmpty(PackageId) && !PackageIdRules.IsValidPackageId(Registry, PackageId))
        {
            yield return new ValidationResult("Invalid package id.", new[] { nameof(PackageId) });
        }

        if (!PackageIdRules.IsValidVersion(CurrentVersion))
            yield return new ValidationResult("Invalid version.", new[] { nameof(CurrentVersion) });

        if (!PackageIdRules.IsValidVersion(TargetVersion))
            yield return new ValidationResult("Invalid version.", new[] { nameof(TargetVersion) });
    }
}

public class AnalysisResult
{
    public string PackageId { get; set; } = string.Empty;
    public string FromVersion { get; set; } = string.Empty;
    public string ToVersion { get; set; } = string.Empty;
    public int TotalVersions { get; set; }
    public List<VersionInfo> Versions { get; set; } = new();
    public Summary Summary { get; set; } = new();
    public PackageTrust? Trust { get; set; }
}

public class PackageMetadata
{
    public string PackageId { get; set; } = string.Empty;
    public string Registry { get; set; } = "nuget";
    public string? LatestVersion { get; set; }
    public string? Description { get; set; }
    public string? Authors { get; set; }
    public string? IconUrl { get; set; }
    public string? ProjectUrl { get; set; }
    public string? GitHubRepositoryUrl { get; set; }
    public string? SourceDirectory { get; set; }
    public List<VersionSummary> Versions { get; set; } = new();
    public PackageTrust? Trust { get; set; }
}

public class VersionSummary
{
    public string Version { get; set; } = string.Empty;
    public DateTime? PublishedDate { get; set; }
    public bool IsPrerelease { get; set; }
    public string ReleaseNotes { get; set; } = string.Empty;
    public string ReleaseNotesHtml { get; set; } = string.Empty;
    public string? ReleaseNotesSource { get; set; }
    public CompiledNotesSource? CompiledFrom { get; set; }
    public List<ReleaseNoteEntry> Entries { get; set; } = new();
    public bool IsEmpty { get; set; } = true;
}

public class VersionInfo
{
    public string Version { get; set; } = string.Empty;
    public DateTime? PublishedDate { get; set; }
    public string ReleaseNotes { get; set; } = string.Empty;
    public string ReleaseNotesHtml { get; set; } = string.Empty;
    public string? ReleaseNotesSource { get; set; }
    public CompiledNotesSource? CompiledFrom { get; set; }
    public List<ReleaseNoteEntry> Entries { get; set; } = new();
    public List<ChangeCategory> Categories { get; set; } = new();
    public bool IsEmpty { get; set; }
    public bool IsPrerelease { get; set; }
}

public class CompiledNotesSource
{
    public string Label { get; set; } = string.Empty;
    public string? Url { get; set; }
}

public class ReleaseNoteEntry
{
    public ChangeCategory Category { get; set; } = ChangeCategory.Other;
    public string Text { get; set; } = string.Empty;

    // Rendered per response only; null (and omitted) in the persisted ParsedNotesJson
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? TextHtml { get; set; }
}

public class Summary
{
    public int BreakingChanges { get; set; }
    public int BugFixes { get; set; }
    public int NewFeatures { get; set; }
    public int PerformanceImprovements { get; set; }
    public int SecurityFixes { get; set; }
    public int Deprecations { get; set; }
    public int OtherChanges { get; set; }
}

public enum ChangeCategory
{
    BreakingChange,
    BugFix,
    NewFeature,
    Performance,
    Security,
    Deprecation,
    Other
}

public class RegistryInfo
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public bool Disabled { get; set; }
}

public class PackageSuggestion
{
    public string PackageId { get; set; } = string.Empty;
    public string Registry { get; set; } = string.Empty;
    public string? LatestVersion { get; set; }
    public string? Description { get; set; }
    public string? IconUrl { get; set; }
}
