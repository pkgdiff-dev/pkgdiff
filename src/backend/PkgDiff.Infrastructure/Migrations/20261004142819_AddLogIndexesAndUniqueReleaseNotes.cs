using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PkgDiff.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLogIndexesAndUniqueReleaseNotes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CachedReleaseNotes_CachedPackageId_Version",
                table: "CachedReleaseNotes");

            // Added by hand: the unique index below cannot be built while a version of a package has
            // several rows. One row per (package, version) is kept: a row with notes before an empty
            // placeholder, then the most recently cached one.
            migrationBuilder.Sql("""
                DELETE FROM "CachedReleaseNotes"
                WHERE "Id" IN (
                    SELECT "Id" FROM (
                        SELECT "Id", ROW_NUMBER() OVER (
                            PARTITION BY "CachedPackageId", "Version"
                            ORDER BY "IsEmpty" ASC, "CachedAt" DESC, "Id" DESC) AS position
                        FROM "CachedReleaseNotes") AS ranked
                    WHERE position > 1);
                """);

            migrationBuilder.CreateIndex(
                name: "IX_ScrapeLogs_CreatedAt",
                table: "ScrapeLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_ScrapeLogs_PackageId_CreatedAt",
                table: "ScrapeLogs",
                columns: new[] { "PackageId", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_RequestLogs_CreatedAt",
                table: "RequestLogs",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_RequestLogs_IpAddress_CreatedAt",
                table: "RequestLogs",
                columns: new[] { "IpAddress", "CreatedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_CachedReleaseNotes_CachedPackageId_Version",
                table: "CachedReleaseNotes",
                columns: new[] { "CachedPackageId", "Version" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlockedIps_IpAddress",
                table: "BlockedIps",
                column: "IpAddress");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ScrapeLogs_CreatedAt",
                table: "ScrapeLogs");

            migrationBuilder.DropIndex(
                name: "IX_ScrapeLogs_PackageId_CreatedAt",
                table: "ScrapeLogs");

            migrationBuilder.DropIndex(
                name: "IX_RequestLogs_CreatedAt",
                table: "RequestLogs");

            migrationBuilder.DropIndex(
                name: "IX_RequestLogs_IpAddress_CreatedAt",
                table: "RequestLogs");

            migrationBuilder.DropIndex(
                name: "IX_CachedReleaseNotes_CachedPackageId_Version",
                table: "CachedReleaseNotes");

            migrationBuilder.DropIndex(
                name: "IX_BlockedIps_IpAddress",
                table: "BlockedIps");

            migrationBuilder.CreateIndex(
                name: "IX_CachedReleaseNotes_CachedPackageId_Version",
                table: "CachedReleaseNotes",
                columns: new[] { "CachedPackageId", "Version" });
        }
    }
}
