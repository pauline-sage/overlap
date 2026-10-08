using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Overlap.Api.Migrations
{
    /// <inheritdoc />
    public partial class IndependentCalendarNotificationTimezone : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "NotificationTimeZone",
                table: "CalendarPreference",
                type: "varchar(100)",
                maxLength: 100,
                nullable: false,
                defaultValue: "UTC")
                .Annotation("MySql:CharSet", "utf8mb4");
            migrationBuilder.Sql("UPDATE CalendarPreference SET NotificationTimeZone = TimeZone");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "NotificationTimeZone",
                table: "CalendarPreference");
        }
    }
}
