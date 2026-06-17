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
    public class TransfersController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public TransfersController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index(int page = 1)
        {
            var userId = _userManager.GetUserId(User);
            var query = _context.Transfers
                .Include(t => t.FromAccount)
                .Include(t => t.ToAccount)
                .Where(t => t.UserId == userId)
                .OrderByDescending(t => t.Date);
            return View(await PaginatedList<Transfer>.CreateAsync(query, page));
        }

        public async Task<IActionResult> Create()
        {
            var userId = _userManager.GetUserId(User);
            ViewData["Accounts"] = await GetAccountListAsync(userId);
            return View(new Transfer { Date = DateTime.Today });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("FromAccountId,ToAccountId,Amount,Date,Description")] Transfer transfer)
        {
            var userId = _userManager.GetUserId(User)!;
            transfer.UserId = userId;
            transfer.YearMonth = transfer.Date.ToString("yyyy-MM");
            ClearServerFieldErrors();

            if (transfer.FromAccountId == transfer.ToAccountId)
                ModelState.AddModelError(nameof(transfer.ToAccountId), "Destination must differ from source.");
            if (transfer.Amount <= 0)
                ModelState.AddModelError(nameof(transfer.Amount), "Amount must be greater than zero.");

            if (ModelState.IsValid)
            {
                var bothOwned = await _context.Accounts.CountAsync(a => a.UserId == userId
                    && (a.Id == transfer.FromAccountId || a.Id == transfer.ToAccountId));
                if (bothOwned != 2)
                    ModelState.AddModelError("", "Invalid account selection.");
            }

            if (!ModelState.IsValid)
            {
                ViewData["Accounts"] = await GetAccountListAsync(userId);
                return View(transfer);
            }

            _context.Add(transfer);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "Transfer recorded.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var userId = _userManager.GetUserId(User);
            var t = await _context.Transfers.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
            if (t == null) return NotFound();
            ViewData["Accounts"] = await GetAccountListAsync(userId);
            return View(t);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,FromAccountId,ToAccountId,Amount,Date,Description")] Transfer transfer)
        {
            if (id != transfer.Id) return NotFound();
            var userId = _userManager.GetUserId(User)!;
            var existing = await _context.Transfers.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
            if (existing == null) return NotFound();

            ClearServerFieldErrors();
            if (transfer.FromAccountId == transfer.ToAccountId)
                ModelState.AddModelError(nameof(transfer.ToAccountId), "Destination must differ from source.");

            if (!ModelState.IsValid)
            {
                ViewData["Accounts"] = await GetAccountListAsync(userId);
                return View(transfer);
            }

            existing.FromAccountId = transfer.FromAccountId;
            existing.ToAccountId = transfer.ToAccountId;
            existing.Amount = transfer.Amount;
            existing.Date = transfer.Date;
            existing.Description = transfer.Description;
            existing.YearMonth = transfer.Date.ToString("yyyy-MM");
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = "Transfer updated.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();
            var userId = _userManager.GetUserId(User);
            var t = await _context.Transfers
                .Include(x => x.FromAccount).Include(x => x.ToAccount)
                .FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
            if (t == null) return NotFound();
            return View(t);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var userId = _userManager.GetUserId(User);
            var t = await _context.Transfers.FirstOrDefaultAsync(x => x.Id == id && x.UserId == userId);
            if (t != null)
            {
                _context.Transfers.Remove(t);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Transfer deleted.";
            }
            return RedirectToAction(nameof(Index));
        }

        private async Task<SelectList> GetAccountListAsync(string? userId)
        {
            var accs = await _context.Accounts
                .Where(a => a.UserId == userId && a.IsActive)
                .OrderBy(a => a.Name)
                .Select(a => new { a.Id, a.Name })
                .ToListAsync();
            return new SelectList(accs, "Id", "Name");
        }

        private void ClearServerFieldErrors()
        {
            var keys = ModelState.Keys
                .Where(k => k.Contains("UserId", StringComparison.OrdinalIgnoreCase)
                         || k.Contains("User", StringComparison.OrdinalIgnoreCase)
                         || k.Contains("Account", StringComparison.OrdinalIgnoreCase)
                            && !k.EndsWith("Id"))
                .ToList();
            foreach (var k in keys) ModelState.Remove(k);
            ModelState.Remove(nameof(Transfer.YearMonth));
        }
    }
}
