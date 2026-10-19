namespace PkgDiff.Shared.DTOs;

public class UpdateBlogRequest
{
    public string Title { get; set; } = string.Empty;
    public string Content { get; set; } = string.Empty;
}

public class BlogPreviewRequest
{
    [System.ComponentModel.DataAnnotations.StringLength(200_000)]
    public string Content { get; set; } = string.Empty;
}

public class LoginRequest
{
    public string Username { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
}

public class LoginResponse
{
    public string Token { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Role { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = new();
    public DateTime ExpiresAt { get; set; }
}

public class DashboardStats
{
    public int TotalSearches { get; set; }
    public int TotalPackagesAnalyzed { get; set; }
    public int CachedPackages { get; set; }
    public List<PopularPackage> TopPackages { get; set; } = new();
    public List<RecentSearch> RecentSearches { get; set; } = new();
    public Dictionary<string, int> SearchesByDay { get; set; } = new();
}

public class PopularPackage
{
    public string PackageId { get; set; } = string.Empty;
    public int SearchCount { get; set; }
}

public class RecentSearch
{
    public int Id { get; set; }
    public string PackageId { get; set; } = string.Empty;
    public string VersionRange { get; set; } = string.Empty;
    public string Registry { get; set; } = string.Empty;
    public DateTime SearchedAt { get; set; }
    public TimeSpan ProcessingTime { get; set; }
    public string IpAddress { get; set; } = string.Empty;
}

public class PaginatedResult<T>
{
    public List<T> Data { get; set; } = new();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int Total { get; set; }
    public int TotalPages { get; set; }
}

public class AdminPackageInfo
{
    public int Id { get; set; }
    public string PackageId { get; set; } = string.Empty;
    public string LatestVersion { get; set; } = string.Empty;
    public string? ReleaseNotesSource { get; set; }
    public int ReleaseNotesCount { get; set; }
    public DateTime CachedAt { get; set; }
    public DateTime ExpiresAt { get; set; }
}

public class AdminRegistryInfo
{
    public int Id { get; set; }
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public bool Disabled { get; set; }
    public int DisplayOrder { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class RegistryCreateRequest
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public bool Disabled { get; set; }
    public int DisplayOrder { get; set; }
}

public class RegistryUpdateRequest
{
    public string Value { get; set; } = string.Empty;
    public string Label { get; set; } = string.Empty;
    public string Icon { get; set; } = string.Empty;
    public bool Disabled { get; set; }
    public int DisplayOrder { get; set; }
}

public class JobsOverview
{
    public long Enqueued { get; set; }
    public long Processing { get; set; }
    public long Succeeded { get; set; }
    public long Failed { get; set; }
    public long Scheduled { get; set; }
    public List<RecurringJobInfo> RecurringJobs { get; set; } = new();
    public List<RecentJobInfo> RecentJobs { get; set; } = new();
    public List<QueuedJobInfo> QueuedJobs { get; set; } = new();
}

public class RecurringJobInfo
{
    public string Id { get; set; } = string.Empty;
    public string Cron { get; set; } = string.Empty;
    public DateTime? LastExecution { get; set; }
    public DateTime? NextExecution { get; set; }
    public string LastJobState { get; set; } = string.Empty;
}

public class RecentJobInfo
{
    public string JobId { get; set; } = string.Empty;
    public string JobName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTime? CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public long? Duration { get; set; }
    public string? ExceptionMessage { get; set; }
    public string? PackageId { get; set; }
    public List<RecentJobInfo> ChildJobs { get; set; } = new();
}

public class QueuedJobInfo
{
    public string JobId { get; set; } = string.Empty;
    public string JobName { get; set; } = string.Empty;
    public string State { get; set; } = string.Empty;
    public DateTime? EnqueuedAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public string? ServerId { get; set; }
    public string? PackageId { get; set; }
}

public class SystemSettingDto
{
    public int Id { get; set; }
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string DataType { get; set; } = "String";
    public string Category { get; set; } = "General";
    public bool IsSecret { get; set; } = false;
    public bool HasValue { get; set; }
}

public class UpdateSettingRequest
{
    public string Value { get; set; } = string.Empty;
    public bool Clear { get; set; }
}

public class AdminUserInfo
{
    public string Id { get; set; } = string.Empty;
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
    public DateTime CreatedAt { get; set; }
    public DateTime? LastLoginAt { get; set; }
    public bool EmailConfirmed { get; set; }
    public bool LockoutEnabled { get; set; }
    public DateTime? LockoutEnd { get; set; }
}

public class CreateUserRequest
{
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string Password { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
}

public class UpdateUserRequest
{
    public string Email { get; set; } = string.Empty;
    public List<string> Roles { get; set; } = new();
}

public class ResetUserPasswordRequest
{
    [System.ComponentModel.DataAnnotations.Required]
    [System.ComponentModel.DataAnnotations.StringLength(100, MinimumLength = 8)]
    public string NewPassword { get; set; } = string.Empty;
}

public class AdminRoleInfo
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public int UserCount { get; set; }
}

public class CreateRoleRequest
{
    public string Name { get; set; } = string.Empty;
}

public class RequestLogInfo
{
    public int Id { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string Endpoint { get; set; } = string.Empty;
    public string Method { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public DateTime CreatedAt { get; set; }
    public string UserAgent { get; set; } = string.Empty;
}

public class BlockedIpInfo
{
    public int Id { get; set; }
    public string IpAddress { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string BlockType { get; set; } = string.Empty;
    public DateTime? ExpiresAt { get; set; }
    public DateTime CreatedAt { get; set; }
    public string CreatedBy { get; set; } = string.Empty;
}

public class BlockIpRequest
{
    public string IpAddress { get; set; } = string.Empty;
    public string Reason { get; set; } = string.Empty;
    public string BlockType { get; set; } = "Temporary";
    public int? DurationMinutes { get; set; }
}


public class IpStatistics
{
    public string IpAddress { get; set; } = string.Empty;
    public int RequestCount { get; set; }
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public List<string> TopEndpoints { get; set; } = new();
    public bool IsBlocked { get; set; }
}

public class ScrapeLogInfo
{
    public int Id { get; set; }
    public string PackageId { get; set; } = string.Empty;
    public string Version { get; set; } = string.Empty;
    public string Registry { get; set; } = string.Empty;
    public string? GitHubRepoUrl { get; set; }
    public string FailureReason { get; set; } = string.Empty;
    public string? AvailableVersions { get; set; }
    public string AttemptedSources { get; set; } = string.Empty;
    public bool IsResolved { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class ScrapeLogSummary
{
    public int TotalFailures { get; set; }
    public int UnresolvedCount { get; set; }
    public List<ScrapeLogPackageSummary> TopFailingPackages { get; set; } = new();
}

public class ScrapeLogPackageSummary
{
    public string PackageId { get; set; } = string.Empty;
    public int FailureCount { get; set; }
    public string? LatestReason { get; set; }
    public DateTime LastOccurrence { get; set; }
}

public class PermissionDto
{
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Group { get; set; } = string.Empty;
    public bool IsSelected { get; set; }
}

public class RolePermissionsDto
{
    public string RoleId { get; set; } = string.Empty;
    public string RoleName { get; set; } = string.Empty;
    public List<PermissionDto> Permissions { get; set; } = new();
}

public class UpdateRolePermissionsRequest
{
    public string RoleId { get; set; } = string.Empty;
    public List<string> Permissions { get; set; } = new();
}

public class SitemapEntry
{
    public string PackageId { get; set; } = string.Empty;
    public string Registry { get; set; } = string.Empty;
    public DateTime? LastModified { get; set; }
    public string ChangeFreq { get; set; } = "weekly";
    public decimal Priority { get; set; } = 0.6m;
}

public class RegistryReorderRequest
{
    public int Id { get; set; }
    public int DisplayOrder { get; set; }
}

