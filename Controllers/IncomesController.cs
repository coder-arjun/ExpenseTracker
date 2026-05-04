using ExpenseTracker.Data;
using ExpenseTracker.Models;
using ExpenseTracker.Models.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
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
        public async Task<IActionResult> Index(int page = 1, string? filterType = null, string? selectedMonth = null, int? selectedYear = null, DateTime? startDate = null, DateTime? endDate = null)
        {
            var userId = _userManager.GetUserId(User);
            var query = _context.Incomes
                .Where(i => i.UserId == userId);

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

            // Pass filter values to view for form persistence and pagination
            ViewData["FilterType"] = filterType;
            ViewData["SelectedMonth"] = selectedMonth;
            ViewData["SelectedYear"] = selectedYear;
            ViewData["StartDate"] = startDate?.ToString("yyyy-MM-dd");
            ViewData["EndDate"] = endDate?.ToString("yyyy-MM-dd");

            query = query.OrderByDescending(i => i.Date);
            return View(await PaginatedList<Income>.CreateAsync(query, page));
        }

        // GET: Incomes/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            var income = await _context.Incomes
                .FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);
            if (income == null) return NotFound();

            return View(income);
        }

        // GET: Incomes/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Incomes/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Amount,Date,Source,YearMonth")] Income income)
        {
            income.UserId = _userManager.GetUserId(User)!;
            ClearServerFieldErrors();
            if (ModelState.IsValid)
            {
                _context.Add(income);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Income created successfully.";
                return RedirectToAction(nameof(Index));
            }
            TempData["ErrorMessage"] = string.Join(" ", ModelState.Values
                .SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
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

            return View(income);
        }

        // POST: Incomes/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Amount,Date,Source,YearMonth")] Income income)
        {
            if (id != income.Id) return NotFound();

            income.UserId = _userManager.GetUserId(User)!;
            ClearServerFieldErrors();

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
            return View(income);
        }

        // GET: Incomes/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            var income = await _context.Incomes
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

        private void ClearServerFieldErrors()
        {
            // Remove all ModelState entries for fields set server-side, regardless of key prefix
            var keysToRemove = ModelState.Keys
                .Where(k => k.Contains("UserId", StringComparison.OrdinalIgnoreCase)
                         || k.Contains("User", StringComparison.OrdinalIgnoreCase))
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
