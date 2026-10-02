using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Overlap.Api.Migrations
{
    /// <inheritdoc />
    public partial class ContestRedrawHistory : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RedrawRequestedBy",
                table: "CookingContest",
                type: "varchar(255)",
                maxLength: 255,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ContestRecipeDraw",
                columns: table => new
                {
                    PairId = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SourceId = table.Column<long>(type: "bigint", nullable: false),
                    ContestId = table.Column<long>(type: "bigint", nullable: false),
                    Content = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContestRecipeDraw", x => new { x.PairId, x.SourceId });
                    table.ForeignKey(
                        name: "FK_ContestRecipeDraw_CookingContest_ContestId",
                        column: x => x.ContestId,
                        principalTable: "CookingContest",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ContestRecipeDraw_ContestId",
                table: "ContestRecipeDraw",
                column: "ContestId");
            // Existing revealed recipes must never be eligible again.
            migrationBuilder.Sql(@"INSERT IGNORE INTO ContestRecipeDraw (PairId, SourceId, ContestId, Content)
                SELECT PairId, CAST(JSON_UNQUOTE(JSON_EXTRACT(RecipeContent, '$.Id')) AS SIGNED), Id, RecipeContent
                FROM CookingContest WHERE RevealedUtc IS NOT NULL AND RecipeContent IS NOT NULL ORDER BY Id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContestRecipeDraw");

            migrationBuilder.DropColumn(
                name: "RedrawRequestedBy",
                table: "CookingContest");
        }
    }
}
