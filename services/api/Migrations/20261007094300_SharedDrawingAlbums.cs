using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Overlap.Api.Migrations
{
    /// <inheritdoc />
    public partial class SharedDrawingAlbums : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "DrawingRound",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    PairId = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    CreatedBy = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Prompt = table.Column<string>(type: "varchar(160)", maxLength: 160, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Minutes = table.Column<int>(type: "int", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    StartUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    EndUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Cancelled = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    Revision = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DrawingRound", x => x.Id);
                    table.ForeignKey(
                        name: "FK_DrawingRound_AspNetUsers_CreatedBy",
                        column: x => x.CreatedBy,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "DrawingPhoto",
                columns: table => new
                {
                    RoundId = table.Column<long>(type: "bigint", nullable: false),
                    AccountId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Png = table.Column<byte[]>(type: "mediumblob", nullable: false),
                    UploadedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    Revision = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OwnerRating = table.Column<int>(type: "int", nullable: true),
                    PartnerRating = table.Column<int>(type: "int", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_DrawingPhoto", x => new { x.RoundId, x.AccountId });
                    table.ForeignKey(
                        name: "FK_DrawingPhoto_AspNetUsers_AccountId",
                        column: x => x.AccountId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_DrawingPhoto_DrawingRound_RoundId",
                        column: x => x.RoundId,
                        principalTable: "DrawingRound",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_DrawingPhoto_AccountId",
                table: "DrawingPhoto",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_DrawingRound_CreatedBy",
                table: "DrawingRound",
                column: "CreatedBy");

            migrationBuilder.CreateIndex(
                name: "IX_DrawingRound_PairId_CreatedUtc",
                table: "DrawingRound",
                columns: new[] { "PairId", "CreatedUtc" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "DrawingPhoto");

            migrationBuilder.DropTable(
                name: "DrawingRound");
        }
    }
}
