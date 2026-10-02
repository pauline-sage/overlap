using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Overlap.Api.Migrations
{
    /// <inheritdoc />
    public partial class PersonalFoodPreferences : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "FoodPreference",
                columns: table => new
                {
                    AccountId = table.Column<string>(type: "varchar(255)", maxLength: 255, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Content = table.Column<string>(type: "varchar(2000)", maxLength: 2000, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    ShareWithPartner = table.Column<bool>(type: "tinyint(1)", nullable: false),
                    SharedPairId = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: true)
                        .Annotation("MySql:CharSet", "utf8mb4"),
                    Revision = table.Column<string>(type: "varchar(32)", maxLength: 32, nullable: false)
                        .Annotation("MySql:CharSet", "utf8mb4")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FoodPreference", x => x.AccountId);
                    table.ForeignKey(
                        name: "FK_FoodPreference_AspNetUsers_AccountId",
                        column: x => x.AccountId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                })
                .Annotation("MySql:CharSet", "utf8mb4");

            // Preserve each person's latest previously saved contest list; sharing starts private.
            migrationBuilder.Sql(@"INSERT INTO FoodPreference (AccountId, Content, ShareWithPartner, Revision)
                SELECT AccountId, Content, 0, REPLACE(UUID(), '-', '') FROM (
                    SELECT AccountId, Content, ROW_NUMBER() OVER (PARTITION BY AccountId ORDER BY ContestId DESC) AS listRank FROM (
                        SELECT ProposedBy AS AccountId, SenderPreferences AS Content, Id AS ContestId FROM CookingContest WHERE SenderPreferences IS NOT NULL
                        UNION ALL
                        SELECT m.AccountId, c.RecipientPreferences, c.Id FROM CookingContest c INNER JOIN PairMembers m ON m.PairId=c.PairId AND m.AccountId<>c.ProposedBy WHERE c.RecipientPreferences IS NOT NULL
                    ) previousLists
                ) latestLists WHERE listRank=1");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "FoodPreference");
        }
    }
}
