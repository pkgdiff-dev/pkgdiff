using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PkgDiff.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class UpdateSocialMediaPostEnums : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(@"
                ALTER TABLE ""SocialMediaPosts"" 
                ALTER COLUMN ""Status"" TYPE integer 
                USING CASE ""Status"" 
                    WHEN 'Pending' THEN 1 
                    WHEN 'Success' THEN 2 
                    WHEN 'Failed' THEN 3 
                    ELSE 1 
                END;
            ");

            migrationBuilder.Sql(@"
                ALTER TABLE ""SocialMediaPosts"" 
                ALTER COLUMN ""Platform"" TYPE integer 
                USING CASE ""Platform"" 
                    WHEN 'Twitter' THEN 1 
                    WHEN 'LinkedIn' THEN 2 
                    ELSE 1 
                END;
            ");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterColumn<string>(
                name: "Status",
                table: "SocialMediaPosts",
                type: "text",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");

            migrationBuilder.AlterColumn<string>(
                name: "Platform",
                table: "SocialMediaPosts",
                type: "text",
                nullable: false,
                oldClrType: typeof(int),
                oldType: "integer");
        }
    }
}
