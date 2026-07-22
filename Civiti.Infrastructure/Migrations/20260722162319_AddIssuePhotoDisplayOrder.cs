using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Civiti.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddIssuePhotoDisplayOrder : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<int>(
                name: "DisplayOrder",
                table: "IssuePhotos",
                type: "integer",
                nullable: false,
                defaultValue: 0);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "DisplayOrder",
                table: "IssuePhotos");
        }
    }
}
