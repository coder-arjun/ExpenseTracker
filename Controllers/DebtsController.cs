using ExpenseTracker.Data;
using ExpenseTracker.Models;
using ExpenseTracker.Models.Domain;
using ExpenseTracker.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Controllers
{
    /// <summary>
    /// Personal IOU ledger: money others owe the user and money the user owes
    /// others, with partial-repayment tracking. Strictly user-scoped.
    /// </summary>
    [Authorize]
    public class DebtsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public DebtsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET: Debts
        public async Task<IActionResult> Index(int page = 1)
        {
            var userId = _userManager.GetUserId(User);
            var all = _context.Debts.Where(d => d.UserId == userId);

            // Summary strip — totals across ALL records, not just the page.
            // Outstanding == Amount - AmountPaid (settled rows contribute 0).
            decimal owedToMe = await all
                .Where(d => d.Direction == DebtDirection.TheyOweMe)
                .SumAsync(d => (decimal?)(d.Amount - d.AmountPaid)) ?? 0m;
            decimal iOwe = await all
                .Where(d => d.Direction == DebtDirection.IOweThem)
                .SumAsync(d => (decimal?)(d.Amount - d.AmountPaid)) ?? 0m;
            int overdue = await all
                .CountAsync(d => d.DueDate != null && d.DueDate < DateTime.Today && d.Amount - d.AmountPaid > 0m);

            ViewData["OwedToMe"] = owedToMe;
            ViewData["IOwe"] = iOwe;
            ViewData["DebtNet"] = owedToMe - iOwe;
            ViewData["OverdueCount"] = overdue;

            // Active (still-owed) records first, then most recent.
            var query = all
                .OrderByDescending(d => d.Amount - d.AmountPaid > 0m)
                .ThenByDescending(d => d.Date);
            return View(await PaginatedList<Debt>.CreateAsync(query, page));
        }

        // GET: Debts/Export?format=csv|xlsx|pdf
        public async Task<IActionResult> Export(string? format = "csv")
        {
            var userId = _userManager.GetUserId(User);
            var rows = await _context.Debts
                .Where(d => d.UserId == userId)
                .OrderByDescending(d => d.Date)
                .ToListAsync();
            var subtitle = $"All time · {rows.Count} record{(rows.Count == 1 ? "" : "s")}";
            var dateStamp = DateTime.Today.ToString("yyyy-MM-dd");

            static string Dir(Debt d) => d.Direction == DebtDirection.TheyOweMe ? "Owed to me" : "I owe";
            static string Stat(Debt d) => d.Status switch
            {
                DebtStatus.Settled => "Settled",
                DebtStatus.PartlyPaid => "Partly paid",
                _ => "Outstanding"
            };

            switch ((format ?? "csv").ToLowerInvariant())
            {
                case "xlsx":
                    var xlsx = ExcelExporter.Build<Debt>("Debts Report", subtitle, rows, new[]
                    {
                        new ExcelExporter.Column<Debt>("Person",      d => d.PersonName),
                        new ExcelExporter.Column<Debt>("Direction",   Dir),
                        new ExcelExporter.Column<Debt>("Amount",      d => d.Amount,        IsCurrency: true),
                        new ExcelExporter.Column<Debt>("Paid",        d => d.AmountPaid,    IsCurrency: true),
                        new ExcelExporter.Column<Debt>("Outstanding", d => d.Outstanding,   IsCurrency: true),
                        new ExcelExporter.Column<Debt>("Status",      Stat),
                        new ExcelExporter.Column<Debt>("Date",        d => d.Date),
                        new ExcelExporter.Column<Debt>("Due",         d => d.DueDate),
                        new ExcelExporter.Column<Debt>("Note",        d => d.Note),
                    }, sheetName: "Debts");
                    return File(xlsx, "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet", $"debts-{dateStamp}.xlsx");

                case "pdf":
                    var pdf = PdfExporter.Build<Debt>("Debts Report", subtitle, rows, new[]
                    {
                        new PdfExporter.Column<Debt>("Person",      d => d.PersonName,                    RelativeWidth: 1.8f),
                        new PdfExporter.Column<Debt>("Direction",   Dir,                                  RelativeWidth: 1.2f),
                        new PdfExporter.Column<Debt>("Amount",      d => d.Amount,      IsCurrency: true, RelativeWidth: 1.1f),
                        new PdfExporter.Column<Debt>("Outstanding", d => d.Outstanding, IsCurrency: true, RelativeWidth: 1.1f),
                        new PdfExporter.Column<Debt>("Status",      Stat,                                 RelativeWidth: 1.0f),
                        new PdfExporter.Column<Debt>("Date",        d => d.Date,                          RelativeWidth: 1.0f),
                    });
                    return File(pdf, "application/pdf", $"debts-{dateStamp}.pdf");

                default: // csv
                    var csv = CsvExporter.Build<Debt>(rows, new (string, Func<Debt, object?>)[]
                    {
                        ("Person",      d => d.PersonName),
                        ("Direction",   d => Dir(d)),
                        ("Amount",      d => d.Amount),
                        ("Paid",        d => d.AmountPaid),
                        ("Outstanding", d => d.Outstanding),
                        ("Status",      d => Stat(d)),
                        ("Date",        d => d.Date),
                        ("Due",         d => d.DueDate),
                        ("Note",        d => d.Note),
                    });
                    return File(csv, "text/csv", $"debts-{dateStamp}.csv");
            }
        }

        // GET: Debts/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            var debt = await _context.Debts
                .FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);
            if (debt == null) return NotFound();

            return View(debt);
        }

        // GET: Debts/Create
        public IActionResult Create()
        {
            return View(new Debt { Date = DateTime.Today, Direction = DebtDirection.TheyOweMe });
        }

        // POST: Debts/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Direction,PersonName,Amount,Date,DueDate,Note")] Debt debt)
        {
            debt.UserId = _userManager.GetUserId(User)!;
            debt.AmountPaid = 0m;
            debt.YearMonth = debt.Date.ToString("yyyy-MM");
            NormalizeServerFields();

            if (ModelState.IsValid)
            {
                _context.Add(debt);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Debt added successfully.";
                return RedirectToAction(nameof(Index));
            }
            TempData["ErrorMessage"] = string.Join(" ", ModelState.Values
                .SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return View(debt);
        }

        // GET: Debts/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            var debt = await _context.Debts
                .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);
            if (debt == null) return NotFound();

            return View(debt);
        }

        // POST: Debts/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Direction,PersonName,Amount,AmountPaid,Date,DueDate,Note")] Debt debt)
        {
            if (id != debt.Id) return NotFound();

            // Verify ownership before trusting the posted row.
            var userId = _userManager.GetUserId(User);
            if (!await _context.Debts.AnyAsync(d => d.Id == id && d.UserId == userId))
                return NotFound();

            debt.UserId = userId!;
            debt.AmountPaid = Math.Clamp(debt.AmountPaid, 0m, debt.Amount);
            debt.YearMonth = debt.Date.ToString("yyyy-MM");
            NormalizeServerFields();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(debt);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!DebtExists(debt.Id))
                        return NotFound();
                    else
                        throw;
                }
                TempData["SuccessMessage"] = "Debt updated successfully.";
                return RedirectToAction(nameof(Index));
            }
            TempData["ErrorMessage"] = string.Join(" ", ModelState.Values
                .SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return View(debt);
        }

        // POST: Debts/Repay/5  — record a (partial) repayment.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Repay(int id, decimal amount)
        {
            var userId = _userManager.GetUserId(User);
            var debt = await _context.Debts
                .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);
            if (debt == null) return NotFound();

            if (amount <= 0m)
            {
                TempData["ErrorMessage"] = "Enter a repayment amount greater than zero.";
                return RedirectToAction(nameof(Details), new { id });
            }

            debt.AmountPaid = Math.Clamp(debt.AmountPaid + amount, 0m, debt.Amount);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = debt.Outstanding <= 0m
                ? $"Recorded — {debt.PersonName}'s balance is now fully settled."
                : $"Recorded {amount.ToString("C0", System.Globalization.CultureInfo.GetCultureInfo("en-IN"))}. {debt.Outstanding.ToString("C0", System.Globalization.CultureInfo.GetCultureInfo("en-IN"))} still outstanding.";
            return RedirectToAction(nameof(Details), new { id });
        }

        // POST: Debts/Settle/5  — mark fully settled in one click.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Settle(int id)
        {
            var userId = _userManager.GetUserId(User);
            var debt = await _context.Debts
                .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);
            if (debt == null) return NotFound();

            debt.AmountPaid = debt.Amount;
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Marked {debt.PersonName}'s debt as fully settled.";
            return RedirectToAction(nameof(Index));
        }

        // GET: Debts/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            var debt = await _context.Debts
                .FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);
            if (debt == null) return NotFound();

            return View(debt);
        }

        // POST: Debts/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var userId = _userManager.GetUserId(User);
            var debt = await _context.Debts
                .FirstOrDefaultAsync(d => d.Id == id && d.UserId == userId);
            if (debt != null)
            {
                _context.Debts.Remove(debt);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Debt deleted successfully.";
            }

            return RedirectToAction(nameof(Index));
        }

        // YearMonth + UserId are set server-side, so drop their binding/validation
        // errors before checking ModelState (mirrors the other CRUD controllers).
        // Note is genuinely optional (the form labels it so), but as a non-nullable
        // string it gets an implicit "required" that rejects an empty value — so drop
        // its error too. It binds to "" which is fine for the NOT NULL column.
        private void NormalizeServerFields()
        {
            foreach (var key in ModelState.Keys
                         .Where(k => k.Contains("UserId", StringComparison.OrdinalIgnoreCase)
                                  || k.Contains("User", StringComparison.OrdinalIgnoreCase)
                                  || k.Contains("YearMonth", StringComparison.OrdinalIgnoreCase)
                                  || k.Equals("Note", StringComparison.OrdinalIgnoreCase))
                         .ToList())
            {
                ModelState.Remove(key);
            }
        }

        private bool DebtExists(int id) => _context.Debts.Any(e => e.Id == id);
    }
}
