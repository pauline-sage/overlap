using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Overlap.Api.Migrations
{
    /// <inheritdoc />
    public partial class PersonalOutingUpdates : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "OutingUpdate",
                columns: table => new
                {
                    PairId = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SenderId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    VenueJson = table.Column<string>(type: "varchar(4000)", maxLength: 4000, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    OnMyWay = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    UpdatedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OutingUpdate", x => new { x.PairId, x.SenderId });
                    table.ForeignKey(
                        name: "FK_OutingUpdate_AspNetUsers_SenderId",
                        column: x => x.SenderId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_OutingUpdate_SenderId",
                table: "OutingUpdate",
                column: "SenderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "OutingUpdate");
        }
    }
}
