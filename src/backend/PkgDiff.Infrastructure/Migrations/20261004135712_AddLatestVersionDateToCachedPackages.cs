using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PkgDiff.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddLatestVersionDateToCachedPackages : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LatestVersionDate",
                table: "CachedPackages",
                type: "timestamp without time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PreviousLatestVersion",
                table: "CachedPackages",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_CachedPackages_LatestVersionDate",
                table: "CachedPackages",
                column: "LatestVersionDate");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_CachedPackages_LatestVersionDate",
                table: "CachedPackages");

            migrationBuilder.DropColumn(
                name: "LatestVersionDate",
                table: "CachedPackages");

            migrationBuilder.DropColumn(
                name: "PreviousLatestVersion",
                table: "CachedPackages");
        }
    }
}
