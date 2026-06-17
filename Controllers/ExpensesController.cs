using ExpenseTracker.Data;
using ExpenseTracker.Models;
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
    public class ExpensesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AttachmentStorage _attachments;

        public ExpensesController(ApplicationDbContext context, UserManager<ApplicationUser> userManager,
                                  AttachmentStorage attachments)
        {
            _context = context;
            _userManager = userManager;
            _attachments = attachments;
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

        // GET: Expenses/Export?format=csv|xlsx|pdf
        public async Task<IActionResult> Export(string? format = "csv",
                                                string? filterType = null, string? selectedMonth = null,
                                                int? selectedYear = null, DateTime? startDate = null,
                                                DateTime? endDate = null, int? selectedCategory = null)
        {
            var userId = _userManager.GetUserId(User);
            var q = _context.Expenses
                .Include(e => e.Category)
                .Include(e => e.Account)
                .Where(e => e.UserId == userId);

            if (selectedCategory.HasValue) q = q.Where(e => e.CategoryId == selectedCategory.Value);
            if (!string.IsNullOrEmpty(filterType))
            {
                switch (filterType)
                {
                    case "Month":
                        if (!string.IsNullOrEmpty(selectedMonth)) q = q.Where(e => e.Month == selectedMonth);
                        break;
                    case "Year":
                        if (selectedYear.HasValue)
                        {
                            var prefix = selectedYear.Value.ToString();
                            q = q.Where(e => e.Month.StartsWith(prefix));
                        }
                        break;
                    case "DateRange":
                        if (startDate.HasValue) q = q.Where(e => e.Date >= startDate.Value);
                        if (endDate.HasValue) q = q.Where(e => e.Date <= endDate.Value);
                        break;
                }
            }

            var rows = await q.OrderByDescending(e => e.Date).ToListAsync();
            var subtitle = BuildSubtitle(filterType, selectedMonth, selectedYear, startDate, endDate, rows.Count, "expense");
            var dateStamp = DateTime.Today.ToString("yyyy-MM-dd");

            switch ((format ?? "csv").ToLowerInvariant())
            {
                case "xlsx":
                    var xlsx = ExcelExporter.Build<Expense>("Expense Report", subtitle, rows, new[]
                    {
                        new ExcelExporter.Column<Expense>("Date",        e => e.Date),
                        new ExcelExporter.Column<Expense>("Amount",      e => e.Amount, IsCurrency: true),
                        new ExcelExporter.Column<Expense>("Category",    e => e.Category?.Name),
                        new ExcelExporter.Column<Expense>("Description", e => e.Description),
                        new ExcelExporter.Column<Expense>("Account",     e => e.Account?.Name),
                        new ExcelExporter.Column<Expense>("Month",       e => e.Month),
                    }, sheetName: "Expenses");
                    return File(xlsx, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"expenses-{dateStamp}.xlsx");

                case "pdf":
                    var pdf = PdfExporter.Build<Expense>("Expense Report", subtitle, rows, new[]
                    {
                        new PdfExporter.Column<Expense>("Date",        e => e.Date,                     RelativeWidth: 1.0f),
                        new PdfExporter.Column<Expense>("Amount",      e => e.Amount, IsCurrency: true, RelativeWidth: 1.2f),
                        new PdfExporter.Column<Expense>("Category",    e => e.Category?.Name,           RelativeWidth: 1.4f),
                        new PdfExporter.Column<Expense>("Description", e => e.Description,              RelativeWidth: 3.0f),
                        new PdfExporter.Column<Expense>("Account",     e => e.Account?.Name,            RelativeWidth: 1.4f),
                        new PdfExporter.Column<Expense>("Month",       e => e.Month,                    RelativeWidth: 1.0f),
                    });
                    return File(pdf, "application/pdf", $"expenses-{dateStamp}.pdf");

                default: // csv
                    var csv = CsvExporter.Build<Expense>(rows, new (string, Func<Expense, object?>)[]
                    {
                        ("Date",        e => e.Date),
                        ("Amount",      e => e.Amount),
                        ("Category",    e => e.Category?.Name),
                        ("Description", e => e.Description),
                        ("Account",     e => e.Account?.Name),
                        ("Month",       e => e.Month),
                    });
                    return File(csv, "text/csv", $"expenses-{dateStamp}.csv");
            }
        }

        private static string BuildSubtitle(string? filterType, string? month, int? year,
                                            DateTime? start, DateTime? end, int count, string unit)
        {
            var period = filterType switch
            {
                "Month"     => $"Month: {month}",
                "Year"      => $"Year: {year}",
                "DateRange" => $"{start:yyyy-MM-dd} → {end:yyyy-MM-dd}",
                _           => "All time"
            };
            return $"{period} · {count} {unit}{(count == 1 ? "" : "s")}";
        }

        // GET: Expenses/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            var expense = await _context.Expenses
                .Include(e => e.Category)
                .Include(e => e.Account)
                .Include(e => e.Attachments)
                .FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);
            if (expense == null) return NotFound();

            return View(expense);
        }

        // GET: Expenses/Create
        public async Task<IActionResult> Create()
        {
            var userId = _userManager.GetUserId(User);
            ViewData["Categories"] = await GetCategoryOptionsAsync(userId, null);
            ViewData["Accounts"] = await GetAccountOptionsAsync(userId, null);
            return View();
        }

        // POST: Expenses/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Amount,Date,Description,CategoryId,AccountId,Month")] Expense expense,
                                                IFormFile? receipt)
        {
            var userId = _userManager.GetUserId(User)!;
            expense.UserId = userId;
            ClearServerFieldErrors();

            await ValidateCategoryOwnershipAsync(expense.CategoryId, CategoryType.Expense, userId, nameof(Expense.CategoryId));
            if (expense.AccountId.HasValue)
                await ValidateAccountOwnershipAsync(expense.AccountId.Value, userId, nameof(Expense.AccountId));

            // Validate the upload (if any) before we commit the expense row.
            if (receipt != null && !_attachments.IsAccepted(receipt, out var why))
                ModelState.AddModelError("receipt", why!);

            if (ModelState.IsValid)
            {
                _context.Add(expense);
                await _context.SaveChangesAsync();

                if (receipt != null)
                {
                    var att = await _attachments.SaveAsync(receipt, userId, expense.Id);
                    _context.Attachments.Add(att);
                    await _context.SaveChangesAsync();
                }

                TempData["SuccessMessage"] = "Expense created successfully.";
                return RedirectToAction(nameof(Index));
            }
            TempData["ErrorMessage"] = string.Join(" ", ModelState.Values
                .SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            ViewData["Categories"] = await GetCategoryOptionsAsync(userId, expense.CategoryId);
            ViewData["Accounts"] = await GetAccountOptionsAsync(userId, expense.AccountId);
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
            ViewData["Accounts"] = await GetAccountOptionsAsync(userId, expense.AccountId);
            return View(expense);
        }

        // POST: Expenses/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Amount,Date,Description,CategoryId,AccountId,Month")] Expense expense,
                                              IFormFile? receipt)
        {
            if (id != expense.Id) return NotFound();

            var userId = _userManager.GetUserId(User)!;
            expense.UserId = userId;
            ClearServerFieldErrors();

            await ValidateCategoryOwnershipAsync(expense.CategoryId, CategoryType.Expense, userId, nameof(Expense.CategoryId));
            if (expense.AccountId.HasValue)
                await ValidateAccountOwnershipAsync(expense.AccountId.Value, userId, nameof(Expense.AccountId));

            if (receipt != null && !_attachments.IsAccepted(receipt, out var why))
                ModelState.AddModelError("receipt", why!);

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(expense);
                    await _context.SaveChangesAsync();

                    if (receipt != null)
                    {
                        var att = await _attachments.SaveAsync(receipt, userId, expense.Id);
                        _context.Attachments.Add(att);
                        await _context.SaveChangesAsync();
                    }
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
            ViewData["Accounts"] = await GetAccountOptionsAsync(userId, expense.AccountId);
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

        // POST: Expenses/BulkDelete — accepts a form with multiple "ids" inputs
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> BulkDelete(int[] ids)
        {
            if (ids == null || ids.Length == 0)
            {
                TempData["WarningMessage"] = "No rows selected.";
                return RedirectToAction(nameof(Index));
            }
            var userId = _userManager.GetUserId(User);
            var toDelete = await _context.Expenses
                .Include(e => e.Attachments)
                .Where(e => e.UserId == userId && ids.Contains(e.Id))
                .ToListAsync();
            if (toDelete.Count == 0)
            {
                TempData["ErrorMessage"] = "Selected rows not found.";
                return RedirectToAction(nameof(Index));
            }
            foreach (var ex in toDelete)
                foreach (var a in ex.Attachments) _attachments.Delete(a);
            _context.Expenses.RemoveRange(toDelete);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Deleted {toDelete.Count} expense{(toDelete.Count == 1 ? "" : "s")}.";
            return RedirectToAction(nameof(Index));
        }

        // POST: Expenses/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var userId = _userManager.GetUserId(User);
            var expense = await _context.Expenses
                .Include(e => e.Attachments)
                .FirstOrDefaultAsync(e => e.Id == id && e.UserId == userId);
            if (expense != null)
            {
                // Wipe attachment files BEFORE the DB cascade deletes their rows.
                foreach (var a in expense.Attachments) _attachments.Delete(a);
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

        private async Task<SelectList> GetAccountOptionsAsync(string? userId, int? selected)
        {
            var accs = await _context.Accounts
                .Where(a => a.UserId == userId && a.IsActive)
                .OrderBy(a => a.Name)
                .Select(a => new { a.Id, a.Name })
                .ToListAsync();
            return new SelectList(accs, "Id", "Name", selected);
        }

        private async Task ValidateAccountOwnershipAsync(int accountId, string userId, string fieldKey)
        {
            var ok = await _context.Accounts.AnyAsync(a => a.Id == accountId && a.UserId == userId);
            if (!ok) ModelState.AddModelError(fieldKey, "Selected account is invalid.");
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
                         || k.Equals("Category", StringComparison.OrdinalIgnoreCase)
                         || k.Equals("Account", StringComparison.OrdinalIgnoreCase))
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
