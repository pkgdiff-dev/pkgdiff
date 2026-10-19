using Microsoft.EntityFrameworkCore;
using PkgDiff.Infrastructure.Data;

namespace PkgDiff.API.Jobs;

public class DatabaseCleanupJob
{
    private readonly ApplicationDbContext _dbContext;

    public DatabaseCleanupJob(ApplicationDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task CleanupOldRequestLogsAsync()
    {
        await _dbContext.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"RequestLogs\" WHERE \"CreatedAt\" < {0}",
            DateTime.UtcNow.AddDays(-30));

        await _dbContext.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"ScrapeLogs\" WHERE \"CreatedAt\" < {0}",
            DateTime.UtcNow.AddDays(-30));

        await _dbContext.Database.ExecuteSqlRawAsync(
            "DELETE FROM \"SearchHistories\" WHERE \"SearchedAt\" < {0}",
            DateTime.UtcNow.AddDays(-90));
    }
}
