using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Overlap.Api.Migrations
{
    /// <inheritdoc />
    public partial class OutingCancellation : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "CancellationReason",
                table: "OutingUpdate",
                type: "varchar(500)",
                maxLength: 500,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "CancelledBy",
                table: "OutingUpdate",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CancellationReason",
                table: "OutingUpdate");

            migrationBuilder.DropColumn(
                name: "CancelledBy",
                table: "OutingUpdate");
        }
    }
}
