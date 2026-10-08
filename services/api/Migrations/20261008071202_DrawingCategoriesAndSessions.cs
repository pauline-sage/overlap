using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Overlap.Api.Migrations
{
    /// <inheritdoc />
    public partial class DrawingCategoriesAndSessions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CategoriesJson",
                table: "DrawingRound",
                type: "varchar(256)",
                maxLength: 256,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<int>(
                name: "DrawingCount",
                table: "DrawingRound",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<int>(
                name: "DrawingNumber",
                table: "DrawingRound",
                type: "int",
                nullable: false,
                defaultValue: 1);

            migrationBuilder.AddColumn<bool>(
                name: "NextCreated",
                table: "DrawingRound",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SessionId",
                table: "DrawingRound",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CategoriesJson",
                table: "DrawingRound");

            migrationBuilder.DropColumn(
                name: "DrawingCount",
                table: "DrawingRound");

            migrationBuilder.DropColumn(
                name: "DrawingNumber",
                table: "DrawingRound");

            migrationBuilder.DropColumn(
                name: "NextCreated",
                table: "DrawingRound");

            migrationBuilder.DropColumn(
                name: "SessionId",
                table: "DrawingRound");
        }
    }
}
