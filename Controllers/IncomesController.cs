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
    public class IncomesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public IncomesController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET: Incomes
        public async Task<IActionResult> Index(int page = 1, string? filterType = null, string? selectedMonth = null, int? selectedYear = null, DateTime? startDate = null, DateTime? endDate = null, int? selectedCategory = null)
        {
            var userId = _userManager.GetUserId(User);
            var query = _context.Incomes
                .Include(i => i.Category)
                .Where(i => i.UserId == userId);

            // Category filter (applies independently of the date filters)
            if (selectedCategory.HasValue)
                query = query.Where(i => i.CategoryId == selectedCategory.Value);

            // Apply filters
            if (!string.IsNullOrEmpty(filterType))
            {
                switch (filterType)
                {
                    case "Month":
                        if (!string.IsNullOrEmpty(selectedMonth))
                            query = query.Where(i => i.YearMonth == selectedMonth);
                        break;
                    case "Year":
                        if (selectedYear.HasValue)
                        {
                            var yearPrefix = selectedYear.Value.ToString();
                            query = query.Where(i => i.YearMonth.StartsWith(yearPrefix));
                        }
                        break;
                    case "DateRange":
                        if (startDate.HasValue)
                            query = query.Where(i => i.Date >= startDate.Value);
                        if (endDate.HasValue)
                            query = query.Where(i => i.Date <= endDate.Value);
                        break;
                }
            }

            // Total over the full filtered set (before paging)
            var filteredTotal = await query.SumAsync(i => (decimal?)i.Amount) ?? 0;
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

            query = query.OrderByDescending(i => i.Date);
            return View(await PaginatedList<Income>.CreateAsync(query, page));
        }

        // GET: Incomes/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            var income = await _context.Incomes
                .Include(i => i.Category)
                .FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);
            if (income == null) return NotFound();

            return View(income);
        }

        // GET: Incomes/Create
        public async Task<IActionResult> Create()
        {
            var userId = _userManager.GetUserId(User);
            ViewData["Categories"] = await GetCategoryOptionsAsync(userId, null);
            return View();
        }

        // POST: Incomes/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Amount,Date,Source,CategoryId,YearMonth")] Income income)
        {
            var userId = _userManager.GetUserId(User)!;
            income.UserId = userId;
            ClearServerFieldErrors();

            // CategoryId is optional for income (nullable). Only validate ownership if supplied.
            if (income.CategoryId.HasValue)
                await ValidateCategoryOwnershipAsync(income.CategoryId.Value, CategoryType.Income, userId, nameof(Income.CategoryId));

            if (ModelState.IsValid)
            {
                _context.Add(income);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Income created successfully.";
                return RedirectToAction(nameof(Index));
            }
            TempData["ErrorMessage"] = string.Join(" ", ModelState.Values
                .SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            ViewData["Categories"] = await GetCategoryOptionsAsync(userId, income.CategoryId);
            return View(income);
        }

        // GET: Incomes/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            var income = await _context.Incomes
                .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId);
            if (income == null) return NotFound();

            ViewData["Categories"] = await GetCategoryOptionsAsync(userId, income.CategoryId);
            return View(income);
        }

        // POST: Incomes/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Amount,Date,Source,CategoryId,YearMonth")] Income income)
        {
            if (id != income.Id) return NotFound();

            var userId = _userManager.GetUserId(User)!;
            income.UserId = userId;
            ClearServerFieldErrors();

            if (income.CategoryId.HasValue)
                await ValidateCategoryOwnershipAsync(income.CategoryId.Value, CategoryType.Income, userId, nameof(Income.CategoryId));

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(income);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!IncomeExists(income.Id))
                        return NotFound();
                    else
                        throw;
                }
                TempData["SuccessMessage"] = "Income updated successfully.";
                return RedirectToAction(nameof(Index));
            }
            TempData["ErrorMessage"] = string.Join(" ", ModelState.Values
                .SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            ViewData["Categories"] = await GetCategoryOptionsAsync(userId, income.CategoryId);
            return View(income);
        }

        // GET: Incomes/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            var income = await _context.Incomes
                .Include(i => i.Category)
                .FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);
            if (income == null) return NotFound();

            return View(income);
        }

        // POST: Incomes/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var userId = _userManager.GetUserId(User);
            var income = await _context.Incomes
                .FirstOrDefaultAsync(i => i.Id == id && i.UserId == userId);
            if (income != null)
            {
                _context.Incomes.Remove(income);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Income deleted successfully.";
            }

            return RedirectToAction(nameof(Index));
        }

        private async Task<SelectList> GetCategoryOptionsAsync(string? userId, int? selected)
        {
            var cats = await _context.Categories
                .Where(c => c.UserId == userId && c.Type == CategoryType.Income)
                .OrderBy(c => c.Name)
                .Select(c => new { c.Id, c.Name })
                .ToListAsync();
            return new SelectList(cats, "Id", "Name", selected);
        }

        private async Task ValidateCategoryOwnershipAsync(int categoryId, CategoryType type, string userId, string fieldKey)
        {
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

        private bool IncomeExists(int id)
        {
            return _context.Incomes.Any(e => e.Id == id);
        }
    }
}
