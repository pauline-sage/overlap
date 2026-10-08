using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Overlap.Api.Migrations
{
    /// <inheritdoc />
    public partial class ConnectionLifecycle : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {


            migrationBuilder.AddColumn<string>(
                name: "CollectionKey",
                table: "SharedPick",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "personal")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ConnectionHistory",
                columns: table => new
                {
                    PairId = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    StartedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Closed = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    DisconnectedUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    ExportExpiresUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ConnectionHistory", x => x.PairId);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.Sql("INSERT INTO ConnectionHistory (PairId, Closed) SELECT DISTINCT a.PairId, NOT EXISTS (SELECT 1 FROM PairMembers m WHERE m.PairId = a.PairId) FROM AlbumAccess a");

            migrationBuilder.CreateIndex(
                name: "IX_SharedPick_ProposedBy_TitleId_CollectionKey",
                table: "SharedPick",
                columns: new[] { "ProposedBy", "TitleId", "CollectionKey" },
                unique: true);
            migrationBuilder.DropIndex(
                name: "IX_SharedPick_ProposedBy_TitleId",
                table: "SharedPick");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_SharedPick_ProposedBy_TitleId",
                table: "SharedPick",
                columns: new[] { "ProposedBy", "TitleId" },
                unique: true);

            migrationBuilder.DropTable(
                name: "ConnectionHistory");

            migrationBuilder.DropIndex(
                name: "IX_SharedPick_ProposedBy_TitleId_CollectionKey",
                table: "SharedPick");

            migrationBuilder.DropColumn(
                name: "CollectionKey",
                table: "SharedPick");


        }
    }
}
