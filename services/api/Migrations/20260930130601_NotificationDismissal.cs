using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Overlap.Api.Migrations
{
    /// <inheritdoc />
    public partial class NotificationDismissal : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "RecipientDismissedRevision",
                table: "SharedPick",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SenderDismissedRevision",
                table: "SharedPick",
                type: "longtext",
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "RecipientDismissedRevision",
                table: "OutingUpdate",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "SenderDismissedRevision",
                table: "OutingUpdate",
                type: "varchar(32)",
                maxLength: 32,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecipientDismissedRevision",
                table: "SharedPick");

            migrationBuilder.DropColumn(
                name: "SenderDismissedRevision",
                table: "SharedPick");

            migrationBuilder.DropColumn(
                name: "RecipientDismissedRevision",
                table: "OutingUpdate");

            migrationBuilder.DropColumn(
                name: "SenderDismissedRevision",
                table: "OutingUpdate");
        }
    }
}
