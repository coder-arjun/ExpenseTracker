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
    public class SavingsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public SavingsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET: Savings
        public async Task<IActionResult> Index(int page = 1)
        {
            var userId = _userManager.GetUserId(User);
            var query = _context.Savings
                .Where(s => s.UserId == userId)
                .OrderByDescending(s => s.Date);
            return View(await PaginatedList<Saving>.CreateAsync(query, page));
        }

        // GET: Savings/Details/5
        public async Task<IActionResult> Details(int? id)
        {
            if (id == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            var saving = await _context.Savings
                .FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);
            if (saving == null) return NotFound();

            return View(saving);
        }

        // GET: Savings/Create
        public IActionResult Create()
        {
            return View();
        }

        // POST: Savings/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Amount,Date,Note,YearMonth")] Saving saving)
        {
            saving.UserId = _userManager.GetUserId(User)!;
            ClearServerFieldErrors();
            if (ModelState.IsValid)
            {
                _context.Add(saving);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Saving created successfully.";
                return RedirectToAction(nameof(Index));
            }
            TempData["ErrorMessage"] = string.Join(" ", ModelState.Values
                .SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return View(saving);
        }

        // GET: Savings/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            var saving = await _context.Savings
                .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
            if (saving == null) return NotFound();

            return View(saving);
        }

        // POST: Savings/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Amount,Date,Note,YearMonth")] Saving saving)
        {
            if (id != saving.Id) return NotFound();

            saving.UserId = _userManager.GetUserId(User)!;
            ClearServerFieldErrors();

            if (ModelState.IsValid)
            {
                try
                {
                    _context.Update(saving);
                    await _context.SaveChangesAsync();
                }
                catch (DbUpdateConcurrencyException)
                {
                    if (!SavingExists(saving.Id))
                        return NotFound();
                    else
                        throw;
                }
                TempData["SuccessMessage"] = "Saving updated successfully.";
                return RedirectToAction(nameof(Index));
            }
            TempData["ErrorMessage"] = string.Join(" ", ModelState.Values
                .SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return View(saving);
        }

        // GET: Savings/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();

            var userId = _userManager.GetUserId(User);
            var saving = await _context.Savings
                .FirstOrDefaultAsync(m => m.Id == id && m.UserId == userId);
            if (saving == null) return NotFound();

            return View(saving);
        }

        // POST: Savings/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var userId = _userManager.GetUserId(User);
            var saving = await _context.Savings
                .FirstOrDefaultAsync(s => s.Id == id && s.UserId == userId);
            if (saving != null)
            {
                _context.Savings.Remove(saving);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Saving deleted successfully.";
            }

            return RedirectToAction(nameof(Index));
        }

        private void ClearServerFieldErrors()
        {
            var keysToRemove = ModelState.Keys
                .Where(k => k.Contains("UserId", StringComparison.OrdinalIgnoreCase)
                         || k.Contains("User", StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var key in keysToRemove)
                ModelState.Remove(key);
        }

        private bool SavingExists(int id)
        {
            return _context.Savings.Any(e => e.Id == id);
        }
    }
}
