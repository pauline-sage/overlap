using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Overlap.Api.Migrations
{
    /// <inheritdoc />
    public partial class ActivityNotificationReadState : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "RecipientHasNews",
                table: "SharedPick",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Revision",
                table: "SharedPick",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "SenderHasNews",
                table: "SharedPick",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Revision",
                table: "OutingUpdate",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "Seen",
                table: "OutingUpdate",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: false);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "RecipientHasNews",
                table: "SharedPick");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "SharedPick");

            migrationBuilder.DropColumn(
                name: "SenderHasNews",
                table: "SharedPick");

            migrationBuilder.DropColumn(
                name: "Revision",
                table: "OutingUpdate");

            migrationBuilder.DropColumn(
                name: "Seen",
                table: "OutingUpdate");
        }
    }
}
