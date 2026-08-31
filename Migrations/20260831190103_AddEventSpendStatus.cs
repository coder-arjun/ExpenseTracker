using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseTracker.Migrations
{
    /// <inheritdoc />
    public partial class AddEventSpendStatus : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // SpendStatus is Paid = 1, Committed = 2. EF's generated default of 0 is not a
            // valid member, so every pre-existing entry would land outside the enum.
            // Everything logged before this migration was money already spent => Paid.
            migrationBuilder.AddColumn<int>(
                name: "Status",
                schema: "finoma",
                table: "EventSpends",
                type: "int",
                nullable: false,
                defaultValue: 1);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Status",
                schema: "finoma",
                table: "EventSpends");
        }
    }
}
