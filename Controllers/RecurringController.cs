using ExpenseTracker.Data;
using ExpenseTracker.Models.Domain;
using ExpenseTracker.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Controllers
{
    [Authorize]
    public class RecurringController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly RecurringProcessor _processor;

        public RecurringController(ApplicationDbContext context, UserManager<ApplicationUser> userManager,
                                   RecurringProcessor processor)
        {
            _context = context;
            _userManager = userManager;
            _processor = processor;
        }

        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User);
            var rules = await _context.RecurringRules
                .Include(r => r.Category)
                .Include(r => r.Account)
                .Where(r => r.UserId == userId)
                .OrderByDescending(r => r.IsActive)
                .ThenBy(r => r.NextDueDate)
                .ToListAsync();
            return View(rules);
        }

        public async Task<IActionResult> Create()
        {
            var userId = _userManager.GetUserId(User);
            await PopulateDropdownsAsync(userId, null, null);
            return View(new RecurringRule
            {
                Type = RecurringType.Expense,
                Frequency = RecurrenceFrequency.Monthly,
                DayOfMonth = DateTime.Today.Day > 28 ? 28 : DateTime.Today.Day,
                StartDate = DateTime.Today,
                NextDueDate = DateTime.Today,
                IsActive = true,
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Name,Type,Amount,Description,Source,CategoryId,AccountId,Frequency,DayOfMonth,StartDate,EndDate,IsActive")] RecurringRule rule)
        {
            var userId = _userManager.GetUserId(User)!;
            rule.UserId = userId;

            await ValidateRuleAsync(rule, userId);

            ClearServerFieldErrors();
            ModelState.Remove(nameof(RecurringRule.NextDueDate));

            if (!ModelState.IsValid)
            {
                await PopulateDropdownsAsync(userId, rule.CategoryId, rule.AccountId);
                return View(rule);
            }

            rule.NextDueDate = ComputeFirstDueDate(rule);

            _context.RecurringRules.Add(rule);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Recurring rule '{rule.Name}' created. Next due {rule.NextDueDate:yyyy-MM-dd}.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var userId = _userManager.GetUserId(User);
            var rule = await _context.RecurringRules.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);
            if (rule == null) return NotFound();
            await PopulateDropdownsAsync(userId, rule.CategoryId, rule.AccountId);
            return View(rule);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name,Type,Amount,Description,Source,CategoryId,AccountId,Frequency,DayOfMonth,StartDate,EndDate,IsActive")] RecurringRule rule)
        {
            if (id != rule.Id) return NotFound();
            var userId = _userManager.GetUserId(User)!;
            var existing = await _context.RecurringRules.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);
            if (existing == null) return NotFound();

            await ValidateRuleAsync(rule, userId);
            ClearServerFieldErrors();
            ModelState.Remove(nameof(RecurringRule.NextDueDate));

            if (!ModelState.IsValid)
            {
                await PopulateDropdownsAsync(userId, rule.CategoryId, rule.AccountId);
                return View(rule);
            }

            existing.Name = rule.Name;
            existing.Type = rule.Type;
            existing.Amount = rule.Amount;
            existing.Description = rule.Description;
            existing.Source = rule.Source;
            existing.CategoryId = rule.CategoryId;
            existing.AccountId = rule.AccountId;
            existing.Frequency = rule.Frequency;
            existing.DayOfMonth = Math.Clamp(rule.DayOfMonth, 1, 28);
            existing.StartDate = rule.StartDate;
            existing.EndDate = rule.EndDate;
            existing.IsActive = rule.IsActive;
            // If schedule changed materially, recompute NextDueDate from the new shape
            // but never push it earlier than tomorrow (avoid backfilling everything).
            existing.NextDueDate = MaxDate(ComputeFirstDueDate(existing), DateTime.Today.AddDays(1));

            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"'{existing.Name}' updated. Next due {existing.NextDueDate:yyyy-MM-dd}.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();
            var userId = _userManager.GetUserId(User);
            var rule = await _context.RecurringRules
                .Include(r => r.Category).Include(r => r.Account)
                .FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);
            if (rule == null) return NotFound();
            return View(rule);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var userId = _userManager.GetUserId(User);
            var rule = await _context.RecurringRules.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);
            if (rule != null)
            {
                _context.RecurringRules.Remove(rule);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Recurring rule deleted.";
            }
            return RedirectToAction(nameof(Index));
        }

        // Pause / Resume — toggle IsActive without bumping NextDueDate.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Toggle(int id)
        {
            var userId = _userManager.GetUserId(User);
            var rule = await _context.RecurringRules.FirstOrDefaultAsync(r => r.Id == id && r.UserId == userId);
            if (rule == null) return NotFound();
            rule.IsActive = !rule.IsActive;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"'{rule.Name}' is now {(rule.IsActive ? "active" : "paused")}.";
            return RedirectToAction(nameof(Index));
        }

        // Run-now button — explicit catch-up trigger
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> RunNow()
        {
            var userId = _userManager.GetUserId(User);
            var posted = await _processor.ProcessForUserAsync(userId!);
            TempData["SuccessMessage"] = posted == 0
                ? "Nothing due."
                : $"Posted {posted} recurring transaction{(posted == 1 ? "" : "s")}.";
            return RedirectToAction(nameof(Index));
        }

        private async Task PopulateDropdownsAsync(string? userId, int? catId, int? accId)
        {
            var expenseCats = await _context.Categories
                .Where(c => c.UserId == userId && c.Type == CategoryType.Expense)
                .OrderBy(c => c.Name)
                .Select(c => new { c.Id, c.Name })
                .ToListAsync();
            ViewData["ExpenseCategories"] = new SelectList(expenseCats, "Id", "Name", catId);

            var incomeCats = await _context.Categories
                .Where(c => c.UserId == userId && c.Type == CategoryType.Income)
                .OrderBy(c => c.Name)
                .Select(c => new { c.Id, c.Name })
                .ToListAsync();
            ViewData["IncomeCategories"] = new SelectList(incomeCats, "Id", "Name", catId);

            var accs = await _context.Accounts
                .Where(a => a.UserId == userId && a.IsActive)
                .OrderBy(a => a.Name)
                .Select(a => new { a.Id, a.Name })
                .ToListAsync();
            ViewData["Accounts"] = new SelectList(accs, "Id", "Name", accId);
        }

        private async Task ValidateRuleAsync(RecurringRule rule, string userId)
        {
            if (rule.Amount <= 0) ModelState.AddModelError(nameof(rule.Amount), "Amount must be greater than zero.");

            if (rule.Type == RecurringType.Expense)
            {
                if (!rule.CategoryId.HasValue)
                    ModelState.AddModelError(nameof(rule.CategoryId), "Pick a category for the expense.");
                else
                {
                    var ok = await _context.Categories.AnyAsync(c => c.Id == rule.CategoryId.Value
                        && c.UserId == userId && c.Type == CategoryType.Expense);
                    if (!ok) ModelState.AddModelError(nameof(rule.CategoryId), "Invalid category.");
                }
            }
            else // Income
            {
                if (string.IsNullOrWhiteSpace(rule.Source))
                    ModelState.AddModelError(nameof(rule.Source), "Income rules need a source (e.g. 'Salary').");
            }

            if (rule.AccountId.HasValue)
            {
                var aOk = await _context.Accounts.AnyAsync(a => a.Id == rule.AccountId.Value && a.UserId == userId);
                if (!aOk) ModelState.AddModelError(nameof(rule.AccountId), "Invalid account.");
            }

            // Clamp day-of-month so we don't hit Feb-30 etc.
            rule.DayOfMonth = Math.Clamp(rule.DayOfMonth, 1, 28);

            if (rule.EndDate.HasValue && rule.EndDate.Value < rule.StartDate)
                ModelState.AddModelError(nameof(rule.EndDate), "End date can't be before start.");
        }

        private void ClearServerFieldErrors()
        {
            var keys = ModelState.Keys
                .Where(k => k.Contains("UserId", StringComparison.OrdinalIgnoreCase)
                         || k.Contains("User", StringComparison.OrdinalIgnoreCase)
                         || k.Equals("Category", StringComparison.OrdinalIgnoreCase)
                         || k.Equals("Account", StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var k in keys) ModelState.Remove(k);
        }

        // First-due date = the first occurrence >= StartDate that matches DayOfMonth.
        private static DateTime ComputeFirstDueDate(RecurringRule rule)
        {
            var start = rule.StartDate.Date;
            var day = Math.Clamp(rule.DayOfMonth, 1, 28);
            var firstThisMonth = new DateTime(start.Year, start.Month, day);
            return firstThisMonth >= start ? firstThisMonth : firstThisMonth.AddMonths(1);
        }

        private static DateTime MaxDate(DateTime a, DateTime b) => a > b ? a : b;
    }
}
