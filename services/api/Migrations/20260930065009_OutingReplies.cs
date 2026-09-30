using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Overlap.Api.Migrations
{
    /// <inheritdoc />
    public partial class OutingReplies : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "ReplyKind",
                table: "OutingUpdate",
                type: "varchar(16)",
                maxLength: 16,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<string>(
                name: "ReplyRevision",
                table: "OutingUpdate",
                type: "varchar(32)",
                maxLength: 32,
                nullable: false,
                defaultValue: "")
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.AddColumn<bool>(
                name: "ReplySeen",
                table: "OutingUpdate",
                type: "tinyint(1)",
                nullable: false,
                defaultValue: true);

            migrationBuilder.AddColumn<string>(
                name: "ReplyVenueJson",
                table: "OutingUpdate",
                type: "varchar(4000)",
                maxLength: 4000,
                nullable: true)
                .Annotation("MySql:CharSet", "utf8mb4");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "ReplyKind",
                table: "OutingUpdate");

            migrationBuilder.DropColumn(
                name: "ReplyRevision",
                table: "OutingUpdate");

            migrationBuilder.DropColumn(
                name: "ReplySeen",
                table: "OutingUpdate");

            migrationBuilder.DropColumn(
                name: "ReplyVenueJson",
                table: "OutingUpdate");
        }
    }
}
