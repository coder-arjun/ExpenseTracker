using ExpenseTracker.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Data
{
    /// <summary>
    /// Seed lists for the per-user Category master. Used by both Register.cshtml.cs
    /// (new sign-ups) and the AddCategoryMaster EF migration (existing users at deploy time).
    /// </summary>
    public static class CategoryDefaults
    {
        // Preserve the values from the original ExpenseCategory enum so existing
        // Expense rows can be back-filled to the matching new Category row by name.
        public static readonly string[] Expense =
        {
            "Food", "Travel", "Bills", "Shopping", "Entertainment",
            "Other", "Tea", "Vehicle", "Marriage", "Loan"
        };

        public static readonly string[] Income =
        {
            "Salary", "Business", "Freelance", "Investment",
            "Rental", "Interest", "Gift", "Bonus", "Other"
        };

        /// <summary>
        /// Seed any missing default categories for a user. Idempotent — safe to call
        /// repeatedly. Returns the number of rows added (the caller must SaveChanges).
        /// </summary>
        public static async Task<int> SeedForUserAsync(ApplicationDbContext db, string userId)
        {
            var existing = await db.Categories
                .Where(c => c.UserId == userId)
                .Select(c => new { c.Type, c.Name })
                .ToListAsync();
            var existingSet = new HashSet<(CategoryType, string)>(
                existing.Select(e => (e.Type, e.Name)));

            var added = 0;
            foreach (var name in Expense)
            {
                if (existingSet.Add((CategoryType.Expense, name)))
                {
                    db.Categories.Add(new Category { Name = name, Type = CategoryType.Expense, UserId = userId });
                    added++;
                }
            }
            foreach (var name in Income)
            {
                if (existingSet.Add((CategoryType.Income, name)))
                {
                    db.Categories.Add(new Category { Name = name, Type = CategoryType.Income, UserId = userId });
                    added++;
                }
            }
            return added;
        }
    }
}
