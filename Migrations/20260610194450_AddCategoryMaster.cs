using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ExpenseTracker.Migrations
{
    /// <inheritdoc />
    /// <remarks>
    /// Migrates Expense/Budget from the legacy ExpenseCategory enum to a per-user
    /// Category master table, and adds optional CategoryId to Income.
    ///
    /// Strategy:
    ///   1. Rename the existing `Category` columns to `CategoryId` (data — the old
    ///      enum int values — is preserved through the rename).
    ///   2. Create the Categories table.
    ///   3. Seed default expense + income categories for every existing user.
    ///   4. Backfill Expenses.CategoryId and Budgets.CategoryId by mapping the
    ///      legacy enum int → the matching user-scoped Category row's new Id.
    ///   5. Only then create the FK constraints (so the backfilled data is valid).
    ///
    /// Down is lossy: the new Category.Id values written to the renamed columns
    /// don't map back to the old enum ints. Schema reverts but data is invalid.
    /// </remarks>
    public partial class AddCategoryMaster : Migration
    {
        // Kept in one place so the CASE backfill stays in sync with CategoryDefaults.cs.
        // Order matches the original ExpenseCategory enum int values.
        private const string ExpenseEnumToName = @"
            CASE {0}
                WHEN 1 THEN 'Food'
                WHEN 2 THEN 'Travel'
                WHEN 3 THEN 'Bills'
                WHEN 4 THEN 'Shopping'
                WHEN 5 THEN 'Entertainment'
                WHEN 6 THEN 'Other'
                WHEN 7 THEN 'Tea'
                WHEN 8 THEN 'Vehicle'
                WHEN 9 THEN 'Marriage'
                WHEN 10 THEN 'Loan'
                ELSE 'Other'
            END";

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // -- 1. Drop the unique composite index that references the old column name --
            migrationBuilder.DropIndex(
                name: "IX_Budgets_UserId_YearMonth_Category",
                table: "Budgets");

            // -- 2. Rename Category -> CategoryId on Expenses & Budgets (preserves data) --
            migrationBuilder.RenameColumn(
                name: "Category",
                table: "Expenses",
                newName: "CategoryId");

            migrationBuilder.RenameColumn(
                name: "Category",
                table: "Budgets",
                newName: "CategoryId");

            // -- 3. Add CategoryId to Incomes (always nullable) --
            migrationBuilder.AddColumn<int>(
                name: "CategoryId",
                table: "Incomes",
                type: "int",
                nullable: true);

            // -- 4. Create the Categories table + supporting indexes --
            migrationBuilder.CreateTable(
                name: "Categories",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    Name = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    Type = table.Column<int>(type: "int", nullable: false),
                    UserId = table.Column<string>(type: "nvarchar(450)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Categories", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Categories_AspNetUsers_UserId",
                        column: x => x.UserId,
                        principalTable: "AspNetUsers",
                        principalColumn: "Id");
                });

            migrationBuilder.CreateIndex(
                name: "IX_Categories_UserId",
                table: "Categories",
                column: "UserId");

            migrationBuilder.CreateIndex(
                name: "IX_Categories_UserId_Type_Name",
                table: "Categories",
                columns: new[] { "UserId", "Type", "Name" },
                unique: true,
                filter: "[UserId] IS NOT NULL");

            // -- 5. Seed default categories for every existing user (idempotent) --
            migrationBuilder.Sql(@"
                -- Expense defaults (Type=1)
                INSERT INTO Categories (Name, Type, UserId)
                SELECT d.Name, 1, u.Id
                FROM AspNetUsers u
                CROSS JOIN (VALUES
                    ('Food'), ('Travel'), ('Bills'), ('Shopping'), ('Entertainment'),
                    ('Other'), ('Tea'), ('Vehicle'), ('Marriage'), ('Loan')
                ) AS d(Name)
                WHERE NOT EXISTS (
                    SELECT 1 FROM Categories c
                    WHERE c.UserId = u.Id AND c.Type = 1 AND c.Name = d.Name
                );

                -- Income defaults (Type=2)
                INSERT INTO Categories (Name, Type, UserId)
                SELECT d.Name, 2, u.Id
                FROM AspNetUsers u
                CROSS JOIN (VALUES
                    ('Salary'), ('Business'), ('Freelance'), ('Investment'),
                    ('Rental'), ('Interest'), ('Gift'), ('Bonus'), ('Other')
                ) AS d(Name)
                WHERE NOT EXISTS (
                    SELECT 1 FROM Categories c
                    WHERE c.UserId = u.Id AND c.Type = 2 AND c.Name = d.Name
                );
            ");

            // -- 6. Backfill Expenses.CategoryId: map legacy enum int -> new Category.Id --
            //       Expenses.CategoryId is non-nullable (required), so every row must match.
            migrationBuilder.Sql($@"
                UPDATE e
                SET e.CategoryId = c.Id
                FROM Expenses e
                JOIN Categories c
                  ON c.UserId = e.UserId
                 AND c.Type = 1
                 AND c.Name = ({string.Format(ExpenseEnumToName, "e.CategoryId")});
            ");

            // -- 7. Backfill Budgets.CategoryId (preserve NULL = Overall budget) --
            migrationBuilder.Sql($@"
                UPDATE b
                SET b.CategoryId = c.Id
                FROM Budgets b
                JOIN Categories c
                  ON c.UserId = b.UserId
                 AND c.Type = 1
                 AND c.Name = ({string.Format(ExpenseEnumToName, "b.CategoryId")})
                WHERE b.CategoryId IS NOT NULL;
            ");

            // -- 8. Now safe to create FK indexes and FK constraints --
            migrationBuilder.CreateIndex(
                name: "IX_Incomes_CategoryId",
                table: "Incomes",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Expenses_CategoryId",
                table: "Expenses",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_CategoryId",
                table: "Budgets",
                column: "CategoryId");

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_UserId_YearMonth_CategoryId",
                table: "Budgets",
                columns: new[] { "UserId", "YearMonth", "CategoryId" },
                unique: true,
                filter: "[UserId] IS NOT NULL AND [CategoryId] IS NOT NULL");

            migrationBuilder.AddForeignKey(
                name: "FK_Budgets_Categories_CategoryId",
                table: "Budgets",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Expenses_Categories_CategoryId",
                table: "Expenses",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_Incomes_Categories_CategoryId",
                table: "Incomes",
                column: "CategoryId",
                principalTable: "Categories",
                principalColumn: "Id",
                onDelete: ReferentialAction.SetNull);
        }

        /// <inheritdoc />
        /// <remarks>
        /// Down reverts schema only — the legacy enum int values are not recoverable
        /// from the new Category.Id values, so post-Down Expense/Budget data will be
        /// inconsistent. Provided for completeness; not expected to be used in prod.
        /// </remarks>
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_Budgets_Categories_CategoryId",
                table: "Budgets");

            migrationBuilder.DropForeignKey(
                name: "FK_Expenses_Categories_CategoryId",
                table: "Expenses");

            migrationBuilder.DropForeignKey(
                name: "FK_Incomes_Categories_CategoryId",
                table: "Incomes");

            migrationBuilder.DropTable(
                name: "Categories");

            migrationBuilder.DropIndex(
                name: "IX_Incomes_CategoryId",
                table: "Incomes");

            migrationBuilder.DropIndex(
                name: "IX_Expenses_CategoryId",
                table: "Expenses");

            migrationBuilder.DropIndex(
                name: "IX_Budgets_CategoryId",
                table: "Budgets");

            migrationBuilder.DropIndex(
                name: "IX_Budgets_UserId_YearMonth_CategoryId",
                table: "Budgets");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                table: "Incomes");

            migrationBuilder.RenameColumn(
                name: "CategoryId",
                table: "Expenses",
                newName: "Category");

            migrationBuilder.RenameColumn(
                name: "CategoryId",
                table: "Budgets",
                newName: "Category");

            migrationBuilder.CreateIndex(
                name: "IX_Budgets_UserId_YearMonth_Category",
                table: "Budgets",
                columns: new[] { "UserId", "YearMonth", "Category" },
                unique: true,
                filter: "[UserId] IS NOT NULL AND [Category] IS NOT NULL");
        }
    }
}
