using Microsoft.EntityFrameworkCore;
using PkgDiff.Domain.Entities;
using PkgDiff.Infrastructure.Data;

namespace PkgDiff.Tests.Content;

public class BlogPostRelationshipTests
{
    [Fact]
    public void SocialMediaPost_BlogPost_foreign_key_is_set_null()
    {
        using var context = CreateContext();

        var foreignKey = Assert.Single(
            context.Model.FindEntityType(typeof(SocialMediaPost))!.GetForeignKeys(),
            fk => fk.PrincipalEntityType.ClrType == typeof(BlogPost));

        Assert.Equal(DeleteBehavior.SetNull, foreignKey.DeleteBehavior);
    }

    [Fact]
    public void Every_foreign_key_into_BlogPosts_is_set_null()
    {
        using var context = CreateContext();

        var foreignKeys = context.Model.GetEntityTypes()
            .SelectMany(e => e.GetForeignKeys())
            .Where(fk => fk.PrincipalEntityType.ClrType == typeof(BlogPost))
            .ToList();

        Assert.NotEmpty(foreignKeys);
        Assert.All(foreignKeys, fk => Assert.Equal(DeleteBehavior.SetNull, fk.DeleteBehavior));
    }

    // Reading the model never opens a connection, so no database is needed
    private static ApplicationDbContext CreateContext() =>
        new(new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql("Host=localhost;Database=pkgdiff_model_only")
            .Options);
}
