using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PkgDiff.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddCachedPackageLastViewedAt : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "LastViewedAt",
                table: "CachedPackages",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "LastViewedAt",
                table: "CachedPackages");
        }
    }
}
