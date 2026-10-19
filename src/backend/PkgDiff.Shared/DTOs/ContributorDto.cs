namespace PkgDiff.Shared.DTOs;

public record ContributorDto(
    string GitHubUsername,
    string GitHubUrl,
    int MentionCount,
    int Rank
);
