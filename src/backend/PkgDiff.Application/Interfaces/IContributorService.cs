using PkgDiff.Shared.DTOs;

namespace PkgDiff.Application.Interfaces;

public interface IContributorService
{
    Task<List<ContributorDto>> GetLeaderboardAsync(int limit = 100);
    Task<(List<ContributorDto> Items, int Total)> GetLeaderboardPagedAsync(int page = 1, int pageSize = 20);
    Task UpsertContributorMentionsAsync(IEnumerable<string> usernames);
    Task BackfillFromExistingReleaseNotesAsync();
}
