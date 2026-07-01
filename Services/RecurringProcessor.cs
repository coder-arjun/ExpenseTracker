using ExpenseTracker.Data;
using ExpenseTracker.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Services
{
    /// <summary>
    /// Catches up due recurring rules — invoked lazily from DashboardController.
    /// For each user, posts an Expense or Income for every missed occurrence
    /// since the rule's NextDueDate, up to today, then advances NextDueDate.
    /// Idempotent: if NextDueDate is in the future, nothing happens.
    /// </summary>
    public class RecurringProcessor
    {
        private readonly ApplicationDbContext _db;

        public RecurringProcessor(ApplicationDbContext db) => _db = db;

        /// <summary>Returns the number of transactions auto-posted.</summary>
        public async Task<int> ProcessForUserAsync(string userId, DateTime? asOf = null)
        {
            var today = (asOf ?? DateTime.Today).Date;

            var dueRules = await _db.RecurringRules
                .Where(r => r.UserId == userId
                         && r.IsActive
                         && r.NextDueDate <= today
                         && (r.EndDate == null || r.NextDueDate <= r.EndDate))
                .ToListAsync();

            if (dueRules.Count == 0) return 0;

            // Rules without their own account fall back to the user's primary account,
            // so recurring transactions still move an account balance (and net worth).
            var primaryAccountId = await _db.Accounts
                .Where(a => a.UserId == userId && a.IsPrimary)
                .Select(a => (int?)a.Id)
                .FirstOrDefaultAsync();

            var posted = 0;
            foreach (var rule in dueRules)
            {
                // Cap the number of catch-up posts per rule per pass — guards against a
                // user reactivating a long-paused rule and getting hundreds of entries.
                const int CatchUpLimit = 12;

                var safety = 0;
                while (rule.NextDueDate <= today && rule.IsActive && safety++ < CatchUpLimit)
                {
                    if (rule.EndDate.HasValue && rule.NextDueDate > rule.EndDate.Value) break;
                    Post(rule, rule.NextDueDate, primaryAccountId);
                    rule.NextDueDate = Advance(rule, rule.NextDueDate);
                    posted++;
                }
            }

            await _db.SaveChangesAsync();
            return posted;
        }

        private void Post(RecurringRule rule, DateTime date, int? fallbackAccountId)
        {
            var yearMonth = date.ToString("yyyy-MM");
            var desc = string.IsNullOrWhiteSpace(rule.Description) ? rule.Name : rule.Description;
            var accountId = rule.AccountId ?? fallbackAccountId;   // rule's account, else primary

            if (rule.Type == RecurringType.Expense)
            {
                _db.Expenses.Add(new Expense
                {
                    Amount = rule.Amount,
                    Date = date,
                    Description = desc,
                    CategoryId = rule.CategoryId ?? throw new InvalidOperationException("Recurring Expense rule needs a CategoryId."),
                    AccountId = accountId,
                    Month = yearMonth,
                    UserId = rule.UserId,
                });
            }
            else // Income
            {
                _db.Incomes.Add(new Income
                {
                    Amount = rule.Amount,
                    Date = date,
                    Source = string.IsNullOrWhiteSpace(rule.Source) ? rule.Name : rule.Source!,
                    CategoryId = rule.CategoryId,
                    AccountId = accountId,
                    YearMonth = yearMonth,
                    UserId = rule.UserId,
                });
            }
        }

        private static DateTime Advance(RecurringRule rule, DateTime from)
        {
            // v1: Monthly only. Weekly/Yearly reserved.
            return rule.Frequency switch
            {
                RecurrenceFrequency.Weekly => from.AddDays(7),
                RecurrenceFrequency.Yearly => from.AddYears(1),
                _                          => from.AddMonths(1),
            };
        }
    }
}
