using ExpenseTracker.Data;
using ExpenseTracker.Models;
using ExpenseTracker.Models.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Rendering;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Controllers
{
    [Authorize]
    public class ExpensesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public ExpensesController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET: Expenses
        public async Task<IActionResult> Index(int page = 1, string? filterType = null, string? selectedMonth = null, int? selectedYear = null, DateTime? startDate = null, DateTime? endDate = null, int? selectedCategory = null)
        {
            var userId = _userManager.GetUserId(User);
            var query = _context.Expenses
                .Include(e => e.Category)
                .Where(e => e.UserId == userId);

            // Category filter (applies independently of the date filters)
            if (selectedCategory.HasValue)
                query = query.Where(e => e.CategoryId == selectedCategory.Value);

            // Apply filters
            if (!string.IsNullOrEmpty(filterType))
            {
                switch (filterType)
                {
                    case "Month":
                        if (!string.IsNullOrEmpty(selectedMonth))
                            query = query.Where(e => e.Month == selectedMonth);
                        break;
                    case "Year":
                        if (selectedYear.HasValue)
                        {
                            var yearPrefix = selectedYear.Value.ToString();
                            query = query.Where(e => e.Month.StartsWith(yearPrefix));
                        }
                        break;
                    case "DateRange":
                        if (startDate.HasValue)
                            query = query.Where(e => e.Date >= startDate.Value);
                        if (endDate.HasValue)
                            query = query.Where(e => e.Date <= endDate.Value);
                        break;
                }
            }

            // Total of the filtered set — computed before paging so it reflects all matches
            var filteredTotal = await query.SumAsync(e => (decimal?)e.Amount) ?? 0;
            var filteredCount = await query.CountAsync();

            // Pass filter values to view for form persistence and pagination
            ViewData["FilterType"] = filterType;
            ViewData["SelectedMonth"] = selectedMonth;
            ViewData["SelectedYear"] = selectedYear;
            ViewData["StartDate"] = startDate?.ToString("yyyy-MM-dd");
            ViewData["EndDate"] = endDate?.ToString("yyyy-MM-dd");
            ViewData["SelectedCategory"] = selectedCategory;
            ViewData["FilteredTotal"] = filteredTotal;
            ViewData["FilteredCount"] = filteredCount;
            ViewData["Categories"] = await GetCategoryOptionsAsync(userId, selectedCategory);
            ViewData["SelectedCategoryName"] = selectedCategory.HasValue
                ? await _context.Categories.Where(c => c.Id == selectedCategory.Value && c.UserId == userId).Select(c => c.Name).FirstOrDefaultAsync()
                : null;

            query = query.OrderByDescending(e => e.Date);
            return View(await PaginatedList<Expense>.CreateAsync(query, page));
        }

        // GET: Expenses/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            var expense = await _context.Expenses
                .Include(e => e.Category)
                .FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);
            if (expense == null) return NotFound();

            return View(expense);
        }

        // GET: Expenses/Create
        public async Task<IActionResult> Create()
        {
            var userId = _userManager.GetUserId(User);
            ViewData["Categories"] = await GetCategoryOptionsAsync(userId, null);
            return View();
        }

        // POST: Expenses/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Amount,Date,Description,CategoryId,Month")] Expense expense)
        {
            var userId = _userManager.GetUserId(User)!;
            expense.UserId = userId;
            ClearServerFieldErrors();

            await ValidateCategoryOwnershipAsync(expense.CategoryId, CategoryType.Expense, userId, nameof(Expense.CategoryId));

            if (ModelState.IsValid)
            {
                _context.Add(expense);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Expense created successfully.";
                return RedirectToAction(nameof(Index));
            }
            TempData["ErrorMessage"] = string.Join(" ", ModelState.Values
                .SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            ViewData["Categories"] = await GetCategoryOptionsAsync(userId, expense.CategoryId);
            return View(expense);
        }

        // GET: Expenses/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            var expense = await _context.Expenses
                .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);
            if (expense == null) return NotFound();

            ViewData["Categories"] = await GetCategoryOptionsAsync(userId, expense.CategoryId);
            return View(expense);
        }

        // POST: Expenses/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Amount,Date,Description,CategoryId,Month")] Expense expense)
        {
            if (id != expense.Id) return NotFound();

            var userId = _userManager.GetUserId(User)!;
            expense.UserId = userId;
            ClearServerFieldErrors();

            await ValidateCategoryOwnershipAsync(expense.CategoryId, CategoryType.Expense, userId, nameof(Expense.CategoryId));

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(expense);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!ExpenseExists(expense.Id))
                        return NotFound();
                    else
                        throw;
                }
                TempData["SuccessMessage"] = "Expense updated successfully.";
                return RedirectToAction(nameof(Index));
            }
            TempData["ErrorMessage"] = string.Join(" ", ModelState.Values
                .SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            ViewData["Categories"] = await GetCategoryOptionsAsync(userId, expense.CategoryId);
            return View(expense);
        }

        // GET: Expenses/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            var expense = await _context.Expenses
                .Include(e => e.Category)
                .FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);
            if (expense == null) return NotFound();

            return View(expense);
        }

        // POST: Expenses/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var userId = _userManager.GetUserId(User);
            var expense = await _context.Expenses
                .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);
            if (expense != null)
            {
                _context.Expenses.Remove(expense);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Expense deleted successfully.";
            }

            return RedirectToAction(nameof(Index));
        }

        // Build SelectList of this user's Expense categories, with optional "selected".
        private async Task<SelectList> GetCategoryOptionsAsync(string? userId, int? selected)
        {
            var cats = await _context.Categories
                .Where(c => c.UserId == userId && c.Type == CategoryType.Expense)
                .OrderBy(c => c.Name)
                .Select(c => new { c.Id, c.Name })
                .ToListAsync();
            return new SelectList(cats, "Id", "Name", selected);
        }

        // Guard against users posting another user's category id (or wrong type).
        private async Task ValidateCategoryOwnershipAsync(int categoryId, CategoryType type, string userId, string fieldKey)
        {
            if (categoryId == 0)
            {
                ModelState.AddModelError(fieldKey, "Please choose a category.");
                return;
            }
            var ok = await _context.Categories.AnyAsync(c =>
                c.Id == categoryId && c.UserId == userId && c.Type == type);
            if (!ok)
                ModelState.AddModelError(fieldKey, "Selected category is invalid.");
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

        private bool ExpenseExists(int id)
        {
            return _context.Expenses.Any(e => e.Id == id);
        }
    }
}
