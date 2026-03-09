using ExpenseTracker.Data;
using ExpenseTracker.Models.Domain;
using ExpenseTracker.Models.ViewModel;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Controllers
{
    [Authorize]
    public class DashboardController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public DashboardController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index(string? filterType, string? selectedMonth, int? selectedYear, DateTime? startDate, DateTime? endDate)
        {
            var userId = _userManager.GetUserId(User);
            filterType ??= "Month";

            // Default month to current
            if (filterType == "Month" && string.IsNullOrEmpty(selectedMonth))
                selectedMonth = DateTime.Now.ToString("yyyy-MM");

            // Default year to current
            if (filterType == "Year" && !selectedYear.HasValue)
                selectedYear = DateTime.Now.Year;

            // Build queries based on filter
            IQueryable<Expense> expenseQuery = _context.Expenses.Where(e => e.UserId == userId);
            IQueryable<Income> incomeQuery = _context.Incomes.Where(i => i.UserId == userId);
            IQueryable<Saving> savingQuery = _context.Savings.Where(s => s.UserId == userId);

            switch (filterType)
            {
                case "Month":
                    expenseQuery = expenseQuery.Where(e => e.Month == selectedMonth);
                    incomeQuery = incomeQuery.Where(i => i.YearMonth == selectedMonth);
                    savingQuery = savingQuery.Where(s => s.YearMonth == selectedMonth);
                    break;
                case "Year":
                    var yearPrefix = selectedYear.ToString();
                    expenseQuery = expenseQuery.Where(e => e.Month.StartsWith(yearPrefix!));
                    incomeQuery = incomeQuery.Where(i => i.YearMonth.StartsWith(yearPrefix!));
                    savingQuery = savingQuery.Where(s => s.YearMonth.StartsWith(yearPrefix!));
                    break;
                case "DateRange":
                    if (startDate.HasValue)
                    {
                        expenseQuery = expenseQuery.Where(e => e.Date >= startDate.Value);
                        incomeQuery = incomeQuery.Where(i => i.Date >= startDate.Value);
                        savingQuery = savingQuery.Where(s => s.Date >= startDate.Value);
                    }
                    if (endDate.HasValue)
                    {
                        expenseQuery = expenseQuery.Where(e => e.Date <= endDate.Value);
                        incomeQuery = incomeQuery.Where(i => i.Date <= endDate.Value);
                        savingQuery = savingQuery.Where(s => s.Date <= endDate.Value);
                    }
                    break;
            }

            // Totals
            var totalIncome = await incomeQuery.SumAsync(i => (decimal?)i.Amount) ?? 0;
            var totalExpense = await expenseQuery.SumAsync(e => (decimal?)e.Amount) ?? 0;
            var totalSaving = await savingQuery.SumAsync(s => (decimal?)s.Amount) ?? 0;

            // Expenses by category
            var expensesByCategory = await expenseQuery
                .GroupBy(e => e.Category)
                .Select(g => new { Category = g.Key, Total = g.Sum(e => e.Amount) })
                .ToDictionaryAsync(g => g.Category.ToString(), g => g.Total);

            // Monthly breakdowns
            var monthlyExpenses = await expenseQuery
                .GroupBy(e => e.Month)
                .Select(g => new { Month = g.Key, Total = g.Sum(e => e.Amount) })
                .OrderBy(g => g.Month)
                .ToDictionaryAsync(g => g.Month, g => g.Total);

            var monthlyIncomes = await incomeQuery
                .GroupBy(i => i.YearMonth)
                .Select(g => new { Month = g.Key, Total = g.Sum(i => i.Amount) })
                .OrderBy(g => g.Month)
                .ToDictionaryAsync(g => g.Month, g => g.Total);

            var monthlySavings = await savingQuery
                .GroupBy(s => s.YearMonth)
                .Select(g => new { Month = g.Key, Total = g.Sum(s => s.Amount) })
                .OrderBy(g => g.Month)
                .ToDictionaryAsync(g => g.Month, g => g.Total);

            // Recent transactions (last 10)
            var recentExpenses = await expenseQuery
                .OrderByDescending(e => e.Date)
                .Take(10)
                .Select(e => new RecentTransaction
                {
                    Date = e.Date,
                    Type = "Expense",
                    Description = e.Description ?? e.Category.ToString(),
                    Amount = e.Amount
                }).ToListAsync();

            var recentIncomes = await incomeQuery
                .OrderByDescending(i => i.Date)
                .Take(10)
                .Select(i => new RecentTransaction
                {
                    Date = i.Date,
                    Type = "Income",
                    Description = i.Source,
                    Amount = i.Amount
                }).ToListAsync();

            var recentSavings = await savingQuery
                .OrderByDescending(s => s.Date)
                .Take(10)
                .Select(s => new RecentTransaction
                {
                    Date = s.Date,
                    Type = "Saving",
                    Description = s.Note,
                    Amount = s.Amount
                }).ToListAsync();

            var recentTransactions = recentExpenses
                .Concat(recentIncomes)
                .Concat(recentSavings)
                .OrderByDescending(t => t.Date)
                .Take(10)
                .ToList();

            var topLeakages = expensesByCategory
                .OrderByDescending(kvp => kvp.Value)
                .Take(5)
                .Select(kvp => new MoneyLeakage
                {
                    Category = kvp.Key,
                    Amount = kvp.Value,
                    Percentage = totalExpense > 0 ? Math.Round(kvp.Value / totalExpense * 100, 1) : 0
                })
                .ToList();

            // Budget alerts (only for month filter)
            var budgetAlerts = new List<BudgetStatusViewModel>();
            if (filterType == "Month" && !string.IsNullOrEmpty(selectedMonth))
            {
                var budgets = await _context.Budgets
                    .Where(b => b.UserId == userId && b.YearMonth == selectedMonth)
                    .ToListAsync();

                foreach (var budget in budgets)
                {
                    var spent = budget.Category.HasValue
                        ? (expensesByCategory.TryGetValue(budget.Category.Value.ToString(), out var catTotal) ? catTotal : 0)
                        : totalExpense;
                    var pct = budget.Amount > 0 ? Math.Round(spent / budget.Amount * 100, 1) : 0;

                    if (pct >= 80)
                    {
                        budgetAlerts.Add(new BudgetStatusViewModel
                        {
                            Id = budget.Id,
                            CategoryName = budget.Category?.ToString() ?? "Overall",
                            BudgetAmount = budget.Amount,
                            ActualSpent = spent,
                            Category = budget.Category
                        });
                    }
                }
            }

            var model = new DashboardViewModel
            {
                TotalIncome = totalIncome,
                TotalExpense = totalExpense,
                TotalSaving = totalSaving,
                NetBalance = totalIncome - totalExpense,
                SavingsRate = totalIncome == 0 ? 0 : (totalSaving / totalIncome) * 100,
                FilterType = filterType,
                SelectedMonth = selectedMonth,
                SelectedYear = selectedYear,
                StartDate = startDate,
                EndDate = endDate,
                ExpensesByCategory = expensesByCategory,
                MonthlyExpenses = monthlyExpenses,
                MonthlyIncomes = monthlyIncomes,
                MonthlySavings = monthlySavings,
                RecentTransactions = recentTransactions,
                TopLeakages = topLeakages,
                BudgetAlerts = budgetAlerts
            };

            return View(model);
        }
    }
}
