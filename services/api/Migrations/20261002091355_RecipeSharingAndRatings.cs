using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Overlap.Api.Migrations
{
    /// <inheritdoc />
    public partial class RecipeSharingAndRatings : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "ShareDismissed",
                table: "SavedRecipe",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "ShareRevision",
                table: "SavedRecipe",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "ShareSeen",
                table: "SavedRecipe",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "SharedBy",
                table: "SavedRecipe",
                type: "varchar(255)",
                maxLength: 255,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "RecipeExperience",
                columns: table => new
                {
                    RecipeId = table.Column<long>(type: "bigint", nullable: false),
                    AccountId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    MadeUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Stars = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RecipeExperience", x => new { x.RecipeId, x.AccountId });
                    table.ForeignKey(
                        name: "FK_RecipeExperience_AspNetUsers_AccountId",
                        column: x => x.AccountId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RecipeExperience_SavedRecipe_RecipeId",
                        column: x => x.RecipeId,
                        principalTable: "SavedRecipe",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_RecipeExperience_AccountId",
                table: "RecipeExperience",
                column: "AccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "RecipeExperience");

            migrationBuilder.DropColumn(
                name: "ShareDismissed",
                table: "SavedRecipe");

            migrationBuilder.DropColumn(
                name: "ShareRevision",
                table: "SavedRecipe");

            migrationBuilder.DropColumn(
                name: "ShareSeen",
                table: "SavedRecipe");

            migrationBuilder.DropColumn(
                name: "SharedBy",
                table: "SavedRecipe");
        }
    }
}
