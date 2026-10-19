using Microsoft.EntityFrameworkCore;
using PkgDiff.Infrastructure.Data;

namespace PkgDiff.API.Jobs;

// Daily Hangfire job: the log tables would otherwise grow without end
public class DatabaseCleanupJob
{
    private readonly ApplicationDbContext _dbContext;

    public DatabaseCleanupJob(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task CleanupOldRequestLogsAsync()
    {
        // RequestLogs older than 30 days
        await _dbContext.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"RequestLogs\" WHERE \"CreatedAt\" < {0}",
            DateTime.UtcNow.AddDays(-30));

        // ScrapeLogs older than 30 days
        await _dbContext.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"ScrapeLogs\" WHERE \"CreatedAt\" < {0}",
            DateTime.UtcNow.AddDays(-30));

        // SearchHistories older than 90 days
        await _dbContext.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"SearchHistories\" WHERE \"SearchedAt\" < {0}",
            DateTime.UtcNow.AddDays(-90));
    }
}
