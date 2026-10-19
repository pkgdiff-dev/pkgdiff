using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PkgDiff.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class SetNullOnSocialMediaPostBlogPostDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SocialMediaPosts_BlogPosts_BlogPostId",
                table: "SocialMediaPosts");

            migrationBuilder.AddForeignKey(
                name: "FK_SocialMediaPosts_BlogPosts_BlogPostId",
                table: "SocialMediaPosts",
                column: "BlogPostId",
                principalTable: "BlogPosts",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_SocialMediaPosts_BlogPosts_BlogPostId",
                table: "SocialMediaPosts");

            migrationBuilder.AddForeignKey(
                name: "FK_SocialMediaPosts_BlogPosts_BlogPostId",
                table: "SocialMediaPosts",
                column: "BlogPostId",
                principalTable: "BlogPosts",
                principalColumn: "Id");
        }
    }
}
