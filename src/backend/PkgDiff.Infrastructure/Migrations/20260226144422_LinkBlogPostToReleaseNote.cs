using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PkgDiff.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class LinkBlogPostToReleaseNote : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_BlogPosts_PackageId_Version",
                table: "BlogPosts");

            migrationBuilder.AddColumn<int>(
                name: "CachedReleaseNoteId",
                table: "BlogPosts",
                type: "integer",
                nullable: true);

            migrationBuilder.CreateIndex(
                name: "IX_BlogPosts_CachedReleaseNoteId",
                table: "BlogPosts",
                column: "CachedReleaseNoteId",
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_BlogPosts_CachedReleaseNotes_CachedReleaseNoteId",
                table: "BlogPosts",
                column: "CachedReleaseNoteId",
                principalTable: "CachedReleaseNotes",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_BlogPosts_CachedReleaseNotes_CachedReleaseNoteId",
                table: "BlogPosts");

            migrationBuilder.DropIndex(
                name: "IX_BlogPosts_CachedReleaseNoteId",
                table: "BlogPosts");

            migrationBuilder.DropColumn(
                name: "CachedReleaseNoteId",
                table: "BlogPosts");

            migrationBuilder.CreateIndex(
                name: "IX_BlogPosts_PackageId_Version",
                table: "BlogPosts",
                columns: new[] { "PackageId", "Version" },
                unique: true);
        }
    }
}
