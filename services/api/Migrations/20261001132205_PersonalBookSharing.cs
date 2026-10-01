using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Overlap.Api.Migrations
{
    /// <inheritdoc />
    public partial class PersonalBookSharing : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReadingEntry_PairId_BookId",
                table: "ReadingEntry");

            migrationBuilder.AddColumn<string>(
                name: "Scope",
                table: "ReadingEntry",
                type: "varchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql("UPDATE ReadingEntry SET Scope = CASE WHEN Status = 'read' THEN 'read' ELSE AddedBy END");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingEntry_PairId_BookId_Scope",
                table: "ReadingEntry",
                columns: new[] { "PairId", "BookId", "Scope" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_ReadingEntry_PairId_BookId_Scope",
                table: "ReadingEntry");

            migrationBuilder.DropColumn(
                name: "Scope",
                table: "ReadingEntry");

            migrationBuilder.CreateIndex(
                name: "IX_ReadingEntry_PairId_BookId",
                table: "ReadingEntry",
                columns: new[] { "PairId", "BookId" },
                unique: true);
        }
    }
}
