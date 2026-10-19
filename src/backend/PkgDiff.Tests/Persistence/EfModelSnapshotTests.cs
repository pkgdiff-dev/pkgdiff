using Microsoft.EntityFrameworkCore;
using PkgDiff.Infrastructure.Data;

namespace PkgDiff.Tests.Persistence;

public class EfModelSnapshotTests
{
    [Fact]
    public void Model_HasNoPendingChanges_AgainstCommittedMigrationSnapshot()
    {
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(
                "Host=localhost;Database=pkgdiff_model_check",
                b => b.MigrationsAssembly("PkgDiff.Infrastructure"))
            .Options;

        using var context = new ApplicationDbContext(options);

        Assert.False(context.Database.HasPendingModelChanges());
    }
}
