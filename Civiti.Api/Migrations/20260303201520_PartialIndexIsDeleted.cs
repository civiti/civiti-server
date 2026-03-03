using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Civiti.Api.Migrations
{
    /// <inheritdoc />
    public partial class PartialIndexIsDeleted : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserProfiles_IsDeleted",
                table: "UserProfiles");

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_IsDeleted",
                table: "UserProfiles",
                column: "IsDeleted",
                filter: "\"IsDeleted\" = true");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_UserProfiles_IsDeleted",
                table: "UserProfiles");

            migrationBuilder.CreateIndex(
                name: "IX_UserProfiles_IsDeleted",
                table: "UserProfiles",
                column: "IsDeleted");
        }
    }
}
