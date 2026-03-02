using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseTracker.Migrations
{
    /// <inheritdoc />
    public partial class UpdateExpenseStructure : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "YearMonth",
                table: "Expenses",
                newName: "Month");

            migrationBuilder.RenameIndex(
                name: "IX_Expenses_UserId_YearMonth",
                table: "Expenses",
                newName: "IX_Expenses_UserId_Month");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.RenameColumn(
                name: "Month",
                table: "Expenses",
                newName: "YearMonth");

            migrationBuilder.RenameIndex(
                name: "IX_Expenses_UserId_Month",
                table: "Expenses",
                newName: "IX_Expenses_UserId_YearMonth");
        }
    }
}
