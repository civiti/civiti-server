using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Civiti.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAdminIssueNotifications : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AdminIssueNotifications",
                columns: table => new
                {
                    IssueId = table.Column<Guid>(type: "uuid", nullable: false),
                    AdminEmail = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: false),
                    EnqueuedAt = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AdminIssueNotifications", x => new { x.IssueId, x.AdminEmail });
                    table.ForeignKey(
                        name: "FK_AdminIssueNotifications_Issues_IssueId",
                        column: x => x.IssueId,
                        principalTable: "Issues",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_AdminIssueNotifications_IssueId",
                table: "AdminIssueNotifications",
                column: "IssueId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AdminIssueNotifications");
        }
    }
}
