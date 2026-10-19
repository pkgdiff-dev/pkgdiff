using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using PkgDiff.Domain.Entities;
using PkgDiff.Infrastructure.Data;

namespace PkgDiff.Tests.Postgres;

public class UtcTimestampTests
{
    private const string LastMigrationWithoutZones = "20261004142819_AddLogIndexesAndUniqueReleaseNotes";

    [Theory]
    [InlineData(DateTimeKind.Utc)]
    [InlineData(DateTimeKind.Unspecified)]
    public void A_date_without_a_kind_is_stored_as_the_utc_time_it_has_always_meant(DateTimeKind kind)
    {
        var value = new DateTime(2026, 3, 1, 22, 30, 0, kind);

        var stored = UtcDateTimeConverter.ToUtc(value);

        Assert.Equal(DateTimeKind.Utc, stored.Kind);
        Assert.Equal(value.Ticks, stored.Ticks);
    }

    [Fact]
    public void A_local_date_is_converted_to_utc_before_it_is_stored()
    {
        var local = new DateTime(2026, 3, 1, 22, 30, 0, DateTimeKind.Local);

        var stored = UtcDateTimeConverter.ToUtc(local);

        Assert.Equal(DateTimeKind.Utc, stored.Kind);
        Assert.Equal(local.ToUniversalTime(), stored);
    }

    [Fact]
    public async Task Dates_of_any_kind_are_written_and_come_back_as_the_same_utc_instant()
    {
        PostgresDatabase.SkipUnlessConfigured();
        await using var database = await PostgresDatabase.CreateAsync();
        var moment = new DateTime(2026, 3, 1, 22, 30, 0, DateTimeKind.Unspecified);

        await using (var context = database.CreateContext())
        {
            context.Registries.Add(new Registry { Value = "npm", Label = "npm", CreatedAt = moment });
            context.BlockedIps.Add(new BlockedIp
            {
                IpAddress = "203.0.113.5",
                Reason = "test",
                CreatedAt = DateTime.SpecifyKind(moment, DateTimeKind.Utc),
                ExpiresAt = DateTime.SpecifyKind(moment, DateTimeKind.Utc).ToLocalTime()
            });
            await context.SaveChangesAsync(TestContext.Current.CancellationToken);
        }

        await using var reading = database.CreateContext();
        var registry = await reading.Registries.SingleAsync(TestContext.Current.CancellationToken);
        var blocked = await reading.BlockedIps.SingleAsync(TestContext.Current.CancellationToken);
        var utc = DateTime.SpecifyKind(moment, DateTimeKind.Utc);

        Assert.Equal((utc, DateTimeKind.Utc), (registry.CreatedAt, registry.CreatedAt.Kind));
        Assert.Equal((utc, DateTimeKind.Utc), (blocked.CreatedAt, blocked.CreatedAt.Kind));
        Assert.Equal((utc, DateTimeKind.Utc), (blocked.ExpiresAt!.Value, blocked.ExpiresAt.Value.Kind));

        Assert.Equal(1, await reading.BlockedIps.CountAsync(b => b.ExpiresAt == moment, TestContext.Current.CancellationToken));
        Assert.Equal(1, await reading.BlockedIps.CountAsync(b => b.ExpiresAt < DateTime.UtcNow, TestContext.Current.CancellationToken));
        Assert.Equal(1, await reading.Registries.CountAsync(r => r.CreatedAt.Date == utc.Date, TestContext.Current.CancellationToken));
    }

    [Fact]
    public async Task The_migration_keeps_every_stored_time_even_when_the_server_zone_is_not_utc()
    {
        PostgresDatabase.SkipUnlessConfigured();
        await using var database = await PostgresDatabase.CreateAsync(LastMigrationWithoutZones);

        await using (var context = database.CreateContext())
        {
            await context.Database.ExecuteSqlRawAsync(
                """
                INSERT INTO "Registries" ("Value", "Label", "Icon", "Disabled", "DisplayOrder", "CreatedAt")
                VALUES ('npm', 'npm', '', false, 1, TIMESTAMP '2026-03-01 22:30:00');
                """,
                TestContext.Current.CancellationToken);

            // New sessions of this database run in a zone three hours ahead of UTC
            var name = context.Database.GetDbConnection().Database;
            await context.Database.ExecuteSqlRawAsync(
                $"""ALTER DATABASE "{name}" SET timezone TO 'Europe/Istanbul';""", TestContext.Current.CancellationToken);
        }
        NpgsqlConnection.ClearAllPools();

        await using (var context = database.CreateContext())
        {
            Assert.Equal("Europe/Istanbul", await ReadSettingAsync(context, "SHOW timezone"));
            await context.Database.MigrateAsync(TestContext.Current.CancellationToken);
            Assert.Equal("timestamp with time zone", await ReadSettingAsync(
                context,
                """SELECT data_type FROM information_schema.columns WHERE table_name = 'Registries' AND column_name = 'CreatedAt'"""));
        }

        await using var reading = database.CreateContext();
        var registry = await reading.Registries.SingleAsync(TestContext.Current.CancellationToken);
        Assert.Equal(new DateTime(2026, 3, 1, 22, 30, 0, DateTimeKind.Utc), registry.CreatedAt);
        Assert.Equal(DateTimeKind.Utc, registry.CreatedAt.Kind);

        await reading.GetService<IMigrator>().MigrateAsync(LastMigrationWithoutZones, TestContext.Current.CancellationToken);
        Assert.Equal("2026-03-01 22:30:00", await ReadSettingAsync(
            reading, """SELECT to_char("CreatedAt", 'YYYY-MM-DD HH24:MI:SS') FROM "Registries" """));
    }

    private static async Task<string?> ReadSettingAsync(ApplicationDbContext context, string sql)
    {
        var connection = context.Database.GetDbConnection();
        if (connection.State != System.Data.ConnectionState.Open)
            await connection.OpenAsync(TestContext.Current.CancellationToken);

        await using var command = connection.CreateCommand();
        command.CommandText = sql;
        return (await command.ExecuteScalarAsync(TestContext.Current.CancellationToken))?.ToString();
    }
}
