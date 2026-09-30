using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Overlap.Api.Migrations
{
    /// <inheritdoc />
    public partial class PrivateSharingConsent : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Create the replacement index before dropping the FK-supporting index.
            migrationBuilder.CreateIndex(
                name: "IX_SharedPick_ProposedBy_TitleId",
                table: "SharedPick",
                columns: new[] { "ProposedBy", "TitleId" },
                unique: true);
            migrationBuilder.DropIndex(
                name: "IX_SharedPick_PairId_TitleId",
                table: "SharedPick");

            migrationBuilder.DropIndex(
                name: "IX_SharedPick_ProposedBy",
                table: "SharedPick");

            migrationBuilder.AlterColumn<string>(
                name: "PairId",
                table: "SharedPick",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true,
                oldClrType: typeof(string),
                oldType: "varchar(32)",
                oldMaxLength: 32)
                .Annotation("MySql:CharSet", "utf8mb4")
                .OldAnnotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "IsShared",
                table: "SharedPick",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "ShareDisplayName",
                table: "AspNetUsers",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SharePhoto",
                table: "AspNetUsers",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<bool>(
                name: "SharePresence",
                table: "AspNetUsers",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.CreateTable(
                name: "SharingGrant",
                columns: table => new
                {
                    PairId = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    SenderId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Category = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Status = table.Column<string>(type: "varchar(16)", maxLength: 16, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SharingGrant", x => new { x.PairId, x.SenderId, x.Category });
                    table.ForeignKey(
                        name: "FK_SharingGrant_AspNetUsers_SenderId",
                        column: x => x.SenderId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");



            migrationBuilder.CreateIndex(
                name: "IX_SharingGrant_SenderId",
                table: "SharingGrant",
                column: "SenderId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            throw new InvalidOperationException("Automatic rollback would restore shared-by-default access and cannot preserve private picks safely. Use a reviewed backup restore procedure.");
        }
    }
}
