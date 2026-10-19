using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PkgDiff.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateCachedPackagesUniqueIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CachedPackages_PackageId",
                table: "CachedPackages");

            migrationBuilder.CreateIndex(
                name: "IX_CachedPackages_PackageId_RegistryId",
                table: "CachedPackages",
                columns: new[] { "PackageId", "RegistryId" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CachedPackages_PackageId_RegistryId",
                table: "CachedPackages");

            migrationBuilder.CreateIndex(
                name: "IX_CachedPackages_PackageId",
                table: "CachedPackages",
                column: "PackageId",
                unique: true);
        }
    }
}
