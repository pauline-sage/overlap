using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Overlap.Api.Migrations
{
    /// <inheritdoc />
    public partial class CookingMemories : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CookingMemory",
                columns: table => new
                {
                    ContestId = table.Column<long>(type: "bigint", nullable: false),
                    RecipeContent = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CookingMemory", x => x.ContestId);
                    table.ForeignKey(
                        name: "FK_CookingMemory_CookingContest_ContestId",
                        column: x => x.ContestId,
                        principalTable: "CookingContest",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "CookingDishPhoto",
                columns: table => new
                {
                    ContestId = table.Column<long>(type: "bigint", nullable: false),
                    AccountId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Png = table.Column<byte[]>(type: "mediumblob", nullable: false),
                    UploadedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Revision = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CookingDishPhoto", x => new { x.ContestId, x.AccountId });
                    table.ForeignKey(
                        name: "FK_CookingDishPhoto_AspNetUsers_AccountId",
                        column: x => x.AccountId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_CookingDishPhoto_CookingMemory_ContestId",
                        column: x => x.ContestId,
                        principalTable: "CookingMemory",
                        principalColumn: "ContestId",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_CookingDishPhoto_AccountId",
                table: "CookingDishPhoto",
                column: "AccountId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CookingDishPhoto");

            migrationBuilder.DropTable(
                name: "CookingMemory");
        }
    }
}
