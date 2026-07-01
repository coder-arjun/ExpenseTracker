using ExpenseTracker.Data;
using ExpenseTracker.Models.Domain;
using ExpenseTracker.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Controllers
{
    [Authorize]
    public class AccountsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly AccountBalanceService _balances;

        public AccountsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager,
                                  AccountBalanceService balances)
        {
            _context = context;
            _userManager = userManager;
            _balances = balances;
        }

        public async Task<IActionResult> Index()
        {
            var userId = _userManager.GetUserId(User)!;
            var accounts = await _context.Accounts
                .Where(a => a.UserId == userId)
                .OrderByDescending(a => a.IsActive)
                .ThenBy(a => a.Name)
                .ToListAsync();

            ViewData["Balances"] = await _balances.GetBalancesAsync(userId);
            return View(accounts);
        }

        public IActionResult Create()
        {
            return View(new Account { Type = AccountType.Cash, IsActive = true });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Name,Type,OpeningBalance,IsActive")] Account account)
        {
            var userId = _userManager.GetUserId(User)!;
            account.UserId = userId;
            account.Name = (account.Name ?? "").Trim();
            ClearServerFieldErrors();

            if (ModelState.IsValid)
            {
                var dup = await _context.Accounts.AnyAsync(a =>
                    a.UserId == userId && a.Name == account.Name);
                if (dup)
                {
                    ModelState.AddModelError(nameof(account.Name), $"You already have an account named '{account.Name}'.");
                }
                else
                {
                    _context.Add(account);
                    await _context.SaveChangesAsync();

                    // Auto-mark the user's first-ever account as primary so debt
                    // settlements always have somewhere to land.
                    if (!await _context.Accounts.AnyAsync(a => a.UserId == userId && a.IsPrimary))
                    {
                        account.IsPrimary = true;
                        await _context.SaveChangesAsync();
                    }

                    TempData["SuccessMessage"] = $"Account '{account.Name}' created.";
                    return RedirectToAction(nameof(Index));
                }
            }
            return View(account);
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var userId = _userManager.GetUserId(User);
            var acc = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
            if (acc == null) return NotFound();
            return View(acc);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name,Type,OpeningBalance,IsActive")] Account account)
        {
            if (id != account.Id) return NotFound();
            var userId = _userManager.GetUserId(User)!;
            var existing = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
            if (existing == null) return NotFound();

            account.Name = (account.Name ?? "").Trim();
            ModelState.Remove(nameof(Account.UserId));
            ModelState.Remove(nameof(Account.User));

            if (!ModelState.IsValid) return View(account);

            var conflict = await _context.Accounts.AnyAsync(a =>
                a.UserId == userId && a.Name == account.Name && a.Id != id);
            if (conflict)
            {
                ModelState.AddModelError(nameof(account.Name), $"You already have an account named '{account.Name}'.");
                return View(account);
            }

            existing.Name = account.Name;
            existing.Type = account.Type;
            existing.OpeningBalance = account.OpeningBalance;
            existing.IsActive = account.IsActive;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Account '{existing.Name}' updated.";
            return RedirectToAction(nameof(Index));
        }

        // POST: Accounts/SetPrimary/5 — make this the (single) primary account.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> SetPrimary(int id)
        {
            var userId = _userManager.GetUserId(User)!;
            var target = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
            if (target == null) return NotFound();

            var mine = await _context.Accounts.Where(a => a.UserId == userId).ToListAsync();
            foreach (var a in mine) a.IsPrimary = (a.Id == id);
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"'{target.Name}' is now your primary account — debt settlements will land here.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();
            var userId = _userManager.GetUserId(User);
            var acc = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
            if (acc == null) return NotFound();

            ViewData["UsageCount"] = await CountUsageAsync(acc.Id);
            return View(acc);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var userId = _userManager.GetUserId(User);
            var acc = await _context.Accounts.FirstOrDefaultAsync(a => a.Id == id && a.UserId == userId);
            if (acc == null) return RedirectToAction(nameof(Index));

            // We allow deletion even if used — the FK is SetNull on transactions.
            // But Transfer FKs are Restrict — block deletion if transfers reference this account.
            var blockedByTransfers = await _context.Transfers.AnyAsync(t =>
                (t.FromAccountId == id || t.ToAccountId == id) && t.UserId == userId);
            if (blockedByTransfers)
            {
                TempData["ErrorMessage"] = "Can't delete this account — it's used in one or more transfers. Delete those first.";
                return RedirectToAction(nameof(Index));
            }

            _context.Accounts.Remove(acc);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Account '{acc.Name}' deleted.";
            return RedirectToAction(nameof(Index));
        }

        private async Task<int> CountUsageAsync(int accountId)
        {
            var e = await _context.Expenses.CountAsync(x => x.AccountId == accountId);
            var i = await _context.Incomes.CountAsync(x => x.AccountId == accountId);
            var t = await _context.Transfers.CountAsync(x => x.FromAccountId == accountId || x.ToAccountId == accountId);
            return e + i + t;
        }

        private void ClearServerFieldErrors()
        {
            var keys = ModelState.Keys
                .Where(k => k.Contains("UserId", StringComparison.OrdinalIgnoreCase)
                         || k.Contains("User", StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var k in keys) ModelState.Remove(k);
        }
    }
}
