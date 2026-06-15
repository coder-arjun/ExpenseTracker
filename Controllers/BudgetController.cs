using ExpenseTracker.Data;
using ExpenseTracker.Models;
using ExpenseTracker.Models.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Controllers
{
    public class BudgetController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public BudgetController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index(int page = 1, string? yearMonth = null)
        {
            var userId = _userManager.GetUserId(User);
            yearMonth ??= DateTime.Now.ToString("yyyy-MM");

            var query = _context.Budgets
                .Include(b => b.Category)
                .Where(b => b.UserId == userId && b.YearMonth == yearMonth)
                .OrderBy(c => c.CategoryId);

            var budgets = await PaginatedList<Budget>.CreateAsync(query, page);

            // Sum expenses per category for this month so the cards can display "spent vs budgeted".
            var expenses = await _context.Expenses
                  .Where(e => e.UserId == userId && e.Month == yearMonth)
                  .GroupBy(e => e.CategoryId)
                  .Select(g => new { CategoryId = g.Key, Total = g.Sum(e => e.Amount) })
                  .ToDictionaryAsync(g => g.CategoryId, g => g.Total);

            var totalSpent = await _context.Expenses
                .Where(e => e.UserId == userId && e.Month == yearMonth)
                .SumAsync(e => (decimal?)e.Amount) ?? 0;

            ViewData["Expenses"] = expenses;
            ViewData["TotalSpent"] = totalSpent;
            ViewData["YearMonth"] = yearMonth;

            return View(budgets);
        }

        public async Task<IActionResult> Create(string? yearMonth)
        {
            var userId = _userManager.GetUserId(User);
            ViewData["YearMonth"] = yearMonth ?? DateTime.Now.ToString("yyyy-MM");
            ViewData["Categories"] = await GetCategoryOptionsAsync(userId, null);
            return View();
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Amount,CategoryId,YearMonth")] Budget budget)
        {
            var userId = _userManager.GetUserId(User)!;
            budget.UserId = userId;
            ClearServerFieldErrors();

            // Null CategoryId means "Overall" budget — allowed. Otherwise validate ownership.
            if (budget.CategoryId.HasValue)
            {
                var ok = await _context.Categories.AnyAsync(c =>
                    c.Id == budget.CategoryId.Value && c.UserId == userId && c.Type == CategoryType.Expense);
                if (!ok)
                    ModelState.AddModelError(nameof(Budget.CategoryId), "Selected category is invalid.");
            }

            if (ModelState.IsValid)
            {
                // Duplicate check (also enforced by unique index)
                var exists = await _context.Budgets.AnyAsync(b =>
                    b.UserId == budget.UserId &&
                    b.YearMonth == budget.YearMonth &&
                    b.CategoryId == budget.CategoryId);

                if (exists)
                {
                    var label = budget.CategoryId.HasValue
                        ? await _context.Categories.Where(c => c.Id == budget.CategoryId.Value).Select(c => c.Name).FirstOrDefaultAsync() ?? "Selected"
                        : "Overall";
                    TempData["ErrorMessage"] = $"A budget for '{label}' already exists for {budget.YearMonth}.";
                    ViewData["YearMonth"] = budget.YearMonth;
                    ViewData["Categories"] = await GetCategoryOptionsAsync(userId, budget.CategoryId);
                    return View(budget);
                }

                _context.Add(budget);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Budget created successfully.";
                return RedirectToAction(nameof(Index), new { yearMonth = budget.YearMonth });
            }

            TempData["ErrorMessage"] = string.Join(" ", ModelState.Values
                .SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            ViewData["YearMonth"] = budget.YearMonth;
            ViewData["Categories"] = await GetCategoryOptionsAsync(userId, budget.CategoryId);
            return View(budget);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            var budget = await _context.Budgets
                .Include(b => b.Category)
                .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);
            if (budget == null) return NotFound();

            return View(budget);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Amount")] Budget budget)
        {
            if (id != budget.Id) return NotFound();

            var userId = _userManager.GetUserId(User);
            var existing = await _context.Budgets
                .Include(b => b.Category)
                .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);
            if (existing == null) return NotFound();

            existing.Amount = budget.Amount;

            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!BudgetExists(budget.Id))
                    return NotFound();
                else
                    throw;
            }

            TempData["SuccessMessage"] = "Budget updated successfully.";
            return RedirectToAction(nameof(Index), new { yearMonth = existing.YearMonth });
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            var budget = await _context.Budgets
                .Include(b => b.Category)
                .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);
            if (budget == null) return NotFound();

            return View(budget);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var userId = _userManager.GetUserId(User);
            var budget = await _context.Budgets
                .FirstOrDefaultAsync(b => b.Id == id && b.UserId == userId);
            if (budget != null)
            {
                var yearMonth = budget.YearMonth;
                _context.Budgets.Remove(budget);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Budget deleted successfully.";
                return RedirectToAction(nameof(Index), new { yearMonth });
            }

            return RedirectToAction(nameof(Index));
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> CopyFromPreviousMonth(string yearMonth)
        {
            var userId = _userManager.GetUserId(User);
            var currentDate = DateTime.ParseExact(yearMonth, "yyyy-MM", null);
            var previousMonth = currentDate.AddMonths(-1).ToString("yyyy-MM");

            var previousBudgets = await _context.Budgets
                .Where(b => b.UserId == userId && b.YearMonth == previousMonth)
                .ToListAsync();

            if (!previousBudgets.Any())
            {
                TempData["ErrorMessage"] = $"No budgets found for {previousMonth} to copy.";
                return RedirectToAction(nameof(Index), new { yearMonth });
            }

            var existingCategoryIds = await _context.Budgets
                .Where(b => b.UserId == userId && b.YearMonth == yearMonth)
                .Select(b => b.CategoryId)
                .ToListAsync();

            var copied = 0;
            foreach (var prev in previousBudgets)
            {
                if (!existingCategoryIds.Contains(prev.CategoryId))
                {
                    _context.Budgets.Add(new Budget
                    {
                        Amount = prev.Amount,
                        CategoryId = prev.CategoryId,
                        YearMonth = yearMonth,
                        UserId = userId
                    });
                    copied++;
                }
            }
            if (copied > 0)
            {
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = $"{copied} budget(s) copied from {previousMonth}.";
            }
            else
            {
                TempData["ErrorMessage"] = "All budgets from the previous month already exist.";
            }

            return RedirectToAction(nameof(Index), new { yearMonth });
        }

        private async Task<SelectList> GetCategoryOptionsAsync(string? userId, int? selected)
        {
            var cats = await _context.Categories
                .Where(c => c.UserId == userId && c.Type == CategoryType.Expense)
                .OrderBy(c => c.Name)
                .Select(c => new { c.Id, c.Name })
                .ToListAsync();
            return new SelectList(cats, "Id", "Name", selected);
        }

        private void ClearServerFieldErrors()
        {
            var keysToRemove = ModelState.Keys
                .Where(k => k.Contains("UserId", StringComparison.OrdinalIgnoreCase)
                         || k.Contains("User", StringComparison.OrdinalIgnoreCase)
                         || k.Equals("Category", StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var key in keysToRemove)
                ModelState.Remove(key);
        }

        private bool BudgetExists(int id)
        {
            return _context.Budgets.Any(b => b.Id == id);
        }
    }
}
