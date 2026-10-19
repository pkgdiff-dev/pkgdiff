using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using PkgDiff.Infrastructure.Data;
using PkgDiff.Infrastructure.Repositories;

namespace PkgDiff.Tests.Postgres;

public sealed class PostgresDatabase : IAsyncDisposable
{
    public const string ConnectionVariable = "PKGDIFF_TEST_POSTGRES";

    private readonly DbContextOptions<ApplicationDbContext> _options;

    private PostgresDatabase(DbContextOptions<ApplicationDbContext> options)
    {
        _options = options;
    }

    public static bool IsConfigured =>
        !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable(ConnectionVariable));

    public static void SkipUnlessConfigured() =>
        Assert.SkipUnless(IsConfigured, $"set {ConnectionVariable} to a PostgreSQL server to run the database tests");

    // A server that has only just started may refuse the first connections
    public static async Task WaitUntilReachableAsync()
    {
        await using var connection = new NpgsqlConnection(Environment.GetEnvironmentVariable(ConnectionVariable));
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await connection.OpenAsync();
                return;
            }
            catch (Exception) when (attempt < 10)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }

    public static async Task<PostgresDatabase> CreateAsync(string? targetMigration = null)
    {
        var connection = new NpgsqlConnectionStringBuilder(Environment.GetEnvironmentVariable(ConnectionVariable))
        {
            Database = $"pkgdiff_test_{Guid.NewGuid():N}"
        };
        var options = new DbContextOptionsBuilder<ApplicationDbContext>()
            .UseNpgsql(connection.ConnectionString, b => b.MigrationsAssembly("PkgDiff.Infrastructure"))
            .Options;

        var database = new PostgresDatabase(options);
        await using var context = database.CreateContext();

        // A server that has only just started may refuse the first connections
        for (var attempt = 1; ; attempt++)
        {
            try
            {
                await context.GetService<IMigrator>().MigrateAsync(targetMigration);
                return database;
            }
            catch (Exception) when (attempt < 10)
            {
                await Task.Delay(TimeSpan.FromSeconds(2));
            }
        }
    }

    public ApplicationDbContext CreateContext() => new(_options);

    public UnitOfWork CreateUnitOfWork() => new(CreateContext());

    public async ValueTask DisposeAsync()
    {
        await using var context = CreateContext();
        await context.Database.EnsureDeletedAsync();
    }
}
