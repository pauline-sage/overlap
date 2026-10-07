using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Overlap.Api.Migrations
{
    /// <inheritdoc />
    public partial class DrawingTimerSeconds : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Minutes",
                table: "DrawingRound",
                newName: "Seconds");
            migrationBuilder.Sql("UPDATE `DrawingRound` SET `Seconds` = `Seconds` * 60");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Seconds",
                table: "DrawingRound",
                newName: "Minutes");
        }
    }
}
