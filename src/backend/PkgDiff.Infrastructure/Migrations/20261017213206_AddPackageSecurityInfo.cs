using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace PkgDiff.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPackageSecurityInfo : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PackageSecurityInfos",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Registry = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    PackageKey = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    PackageId = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    DataJson = table.Column<string>(type: "jsonb", nullable: false),
                    FetchedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    ExpiresAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    RequestedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    LastError = table.Column<string>(type: "character varying(1024)", maxLength: 1024, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PackageSecurityInfos", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_PackageSecurityInfos_ExpiresAt",
                table: "PackageSecurityInfos",
                column: "ExpiresAt");

            migrationBuilder.CreateIndex(
                name: "IX_PackageSecurityInfos_Registry_PackageKey",
                table: "PackageSecurityInfos",
                columns: new[] { "Registry", "PackageKey" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "PackageSecurityInfos");
        }
    }
}
