using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Civiti.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddReportConfigHardening : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Reports_UserProfiles_ReporterId",
                table: "Reports");

            migrationBuilder.CreateIndex(
                name: "IX_Reports_ReporterId_CreatedAt",
                table: "Reports",
                columns: new[] { "ReporterId", "CreatedAt" });

            migrationBuilder.AddCheckConstraint(
                name: "CK_Reports_TargetType",
                table: "Reports",
                sql: "\"TargetType\" IN ('Issue', 'Comment')");

            migrationBuilder.AddForeignKey(
                name: "FK_Reports_UserProfiles_ReporterId",
                table: "Reports",
                column: "ReporterId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Reports_UserProfiles_ReporterId",
                table: "Reports");

            migrationBuilder.DropIndex(
                name: "IX_Reports_ReporterId_CreatedAt",
                table: "Reports");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Reports_TargetType",
                table: "Reports");

            migrationBuilder.AddForeignKey(
                name: "FK_Reports_UserProfiles_ReporterId",
                table: "Reports",
                column: "ReporterId",
                principalTable: "UserProfiles",
                principalColumn: "Id",
                onDelete: ReferentialAction.Cascade);
        }
    }
}
