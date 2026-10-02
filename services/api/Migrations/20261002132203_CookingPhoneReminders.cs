using System;
using Microsoft.EntityFrameworkCore.Metadata;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Overlap.Api.Migrations
{
    /// <inheritdoc />
    public partial class CookingPhoneReminders : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "PhoneSubscription",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("MySql:ValueGenerationStrategy", MySqlValueGenerationStrategy.IdentityColumn),
                    AccountId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    EndpointHash = table.Column<string>(type: "varchar(64)", maxLength: 64, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ProtectedSubscription = table.Column<string>(type: "longtext", nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PhoneSubscription", x => x.Id);
                    table.ForeignKey(
                        name: "FK_PhoneSubscription_AspNetUsers_AccountId",
                        column: x => x.AccountId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateTable(
                name: "ContestReminderDelivery",
                columns: table => new
                {
                    ContestId = table.Column<long>(type: "bigint", nullable: false),
                    SubscriptionId = table.Column<long>(type: "bigint", nullable: false),
                    NextAttemptUtc = table.Column<DateTime>(type: "datetime(6)", nullable: false),
                    SentUtc = table.Column<DateTime>(type: "datetime(6)", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ContestReminderDelivery", x => new { x.ContestId, x.SubscriptionId });
                    table.ForeignKey(
                        name: "FK_ContestReminderDelivery_CookingContest_ContestId",
                        column: x => x.ContestId,
                        principalTable: "CookingContest",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ContestReminderDelivery_PhoneSubscription_SubscriptionId",
                        column: x => x.SubscriptionId,
                        principalTable: "PhoneSubscription",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            migrationBuilder.CreateIndex(
                name: "IX_ContestReminderDelivery_SubscriptionId",
                table: "ContestReminderDelivery",
                column: "SubscriptionId");

            migrationBuilder.CreateIndex(
                name: "IX_PhoneSubscription_AccountId",
                table: "PhoneSubscription",
                column: "AccountId");

            migrationBuilder.CreateIndex(
                name: "IX_PhoneSubscription_EndpointHash",
                table: "PhoneSubscription",
                column: "EndpointHash",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ContestReminderDelivery");

            migrationBuilder.DropTable(
                name: "PhoneSubscription");
        }
    }
}
