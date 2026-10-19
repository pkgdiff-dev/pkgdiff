using Microsoft.EntityFrameworkCore;
using PkgDiff.Application.Interfaces;
using PkgDiff.Domain.Entities;
using PkgDiff.Infrastructure.Data;
using PkgDiff.Shared.DTOs;

namespace PkgDiff.Infrastructure.Services;

public class ContributorService : IContributorService
{
    private readonly ApplicationDbContext _dbContext;
    private readonly IAnalyzerService _analyzerService;
    private readonly GitHubUserValidatorService _validator;

    public ContributorService(ApplicationDbContext dbContext, IAnalyzerService analyzerService, GitHubUserValidatorService validator)
    {
        _dbContext = dbContext;
        _analyzerService = analyzerService;
        _validator = validator;
    }

    public async Task<List<ContributorDto>> GetLeaderboardAsync(int limit = 100)
    {
        var contributors = await _dbContext.Contributors
            .OrderByDescending(c => c.MentionCount)
            .Take(limit)
            .ToListAsync();

        return contributors
            .Select((c, i) => new ContributorDto(
                c.GitHubUsername,
                $"https://github.com/{c.GitHubUsername}",
                c.MentionCount,
                i + 1
            ))
            .ToList();
    }

    public async Task<(List<ContributorDto> Items, int Total)> GetLeaderboardPagedAsync(int page = 1, int pageSize = 20)
    {
        var total = await _dbContext.Contributors.CountAsync();
        var contributors = await _dbContext.Contributors
            .OrderByDescending(c => c.MentionCount)
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Global rank = position in full ordered set
        var offset = (page - 1) * pageSize;
        var items = contributors
            .Select((c, i) => new ContributorDto(
                c.GitHubUsername,
                $"https://github.com/{c.GitHubUsername}",
                c.MentionCount,
                offset + i + 1
            ))
            .ToList();

        return (items, total);
    }

    public async Task UpsertContributorMentionsAsync(IEnumerable<string> usernames)
    {
        var now = DateTime.UtcNow;

        foreach (var username in usernames)
        {
            // Validate against GitHub API - skip if not a real user
            if (!await _validator.IsValidGitHubUserAsync(username))
                continue;

            var existing = await _dbContext.Contributors
                .FirstOrDefaultAsync(c => c.GitHubUsername.ToLower() == username.ToLower());

            if (existing == null)
            {
                _dbContext.Contributors.Add(new Contributor
                {
                    GitHubUsername = username,
                    MentionCount = 1,
                    FirstSeenAt = now,
                    LastSeenAt = now
                });
            }
            else
            {
                existing.MentionCount++;
                existing.LastSeenAt = now;
            }
        }

        await _dbContext.SaveChangesAsync();
    }

    public async Task BackfillFromExistingReleaseNotesAsync()
    {
        // Reset all counts so backfill is idempotent
        await _dbContext.Database.ExecuteSqlRawAsync("DELETE FROM \"Contributors\"");

        var releaseNotes = await _dbContext.CachedReleaseNotes
            .Where(rn => rn.ReleaseNotesSource == "GitHub" && !string.IsNullOrEmpty(rn.ReleaseNotes))
            .ToListAsync();

        var mentionCounts = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        var firstSeen = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        foreach (var note in releaseNotes)
        {
            var mentions = _analyzerService.ExtractMentions(note.ReleaseNotes);
            foreach (var username in mentions)
            {
                // Validate against GitHub API during backfill too
                if (!await _validator.IsValidGitHubUserAsync(username))
                    continue;

                mentionCounts[username] = mentionCounts.GetValueOrDefault(username) + 1;
                if (!firstSeen.ContainsKey(username))
                    firstSeen[username] = DateTime.UtcNow;
            }
        }

        var now = DateTime.UtcNow;
        foreach (var (username, count) in mentionCounts)
        {
            _dbContext.Contributors.Add(new Contributor
            {
                GitHubUsername = username,
                MentionCount = count,
                FirstSeenAt = firstSeen[username],
                LastSeenAt = now
            });
        }

        await _dbContext.SaveChangesAsync();
    }
}
