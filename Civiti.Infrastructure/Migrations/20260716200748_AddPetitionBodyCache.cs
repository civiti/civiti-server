using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Civiti.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddPetitionBodyCache : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "PetitionBodyContentHash",
                table: "Issues",
                type: "character varying(64)",
                maxLength: 64,
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "PetitionBodyCore",
                table: "Issues",
                type: "text",
                nullable: true);

            migrationBuilder.AddColumn<DateTime>(
                name: "PetitionBodyGeneratedAt",
                table: "Issues",
                type: "timestamp with time zone",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "PetitionBodyContentHash",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "PetitionBodyCore",
                table: "Issues");

            migrationBuilder.DropColumn(
                name: "PetitionBodyGeneratedAt",
                table: "Issues");
        }
    }
}
