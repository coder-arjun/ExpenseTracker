using ExpenseTracker.Data;
using ExpenseTracker.Models.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Controllers
{
    /// <summary>
    /// Three-step welcome flow for new users. Drives the user toward the first
    /// real action (logging an expense) while explaining the seeded categories.
    ///
    /// We don't persist a "onboarded" flag on ApplicationUser — instead, the
    /// flow is reachable on demand from any user; the post-registration path
    /// in Register.cshtml.cs sends fresh accounts here automatically.
    /// </summary>
    [Authorize]
    public class OnboardingController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public OnboardingController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // Step 1 — welcome + seeded categories overview
        public async Task<IActionResult> Welcome()
        {
            var userId = _userManager.GetUserId(User);
            ViewData["ExpenseCats"] = await _context.Categories
                .Where(c => c.UserId == userId && c.Type == CategoryType.Expense)
                .OrderBy(c => c.Name)
                .Select(c => c.Name)
                .ToListAsync();
            ViewData["IncomeCats"] = await _context.Categories
                .Where(c => c.UserId == userId && c.Type == CategoryType.Income)
                .OrderBy(c => c.Name)
                .Select(c => c.Name)
                .ToListAsync();
            return View();
        }

        // Step 2 — record the first expense
        public async Task<IActionResult> FirstExpense()
        {
            var userId = _userManager.GetUserId(User);
            ViewData["Categories"] = await _context.Categories
                .Where(c => c.UserId == userId && c.Type == CategoryType.Expense)
                .OrderBy(c => c.Name)
                .Select(c => new { c.Id, c.Name })
                .ToListAsync();
            return View(new Expense
            {
                Amount = 0,
                Date = DateTime.Today,
                CategoryId = 0,
                Month = DateTime.Today.ToString("yyyy-MM"),
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> FirstExpense([Bind("Amount,Date,Description,CategoryId")] Expense expense)
        {
            var userId = _userManager.GetUserId(User)!;
            expense.UserId = userId;
            expense.Month = expense.Date.ToString("yyyy-MM");

            // Allow user to skip — empty form → straight to Done
            if (expense.Amount <= 0 && expense.CategoryId == 0)
                return RedirectToAction(nameof(Done));

            // Validate category ownership
            var ok = await _context.Categories.AnyAsync(c =>
                c.Id == expense.CategoryId && c.UserId == userId && c.Type == CategoryType.Expense);
            if (!ok)
            {
                ModelState.AddModelError(nameof(expense.CategoryId), "Pick a category.");
            }
            if (expense.Amount <= 0)
            {
                ModelState.AddModelError(nameof(expense.Amount), "Amount must be greater than zero.");
            }

            // Strip server-set fields from validation
            ModelState.Remove(nameof(Expense.UserId));
            ModelState.Remove(nameof(Expense.User));
            ModelState.Remove(nameof(Expense.Month));

            if (!ModelState.IsValid)
            {
                ViewData["Categories"] = await _context.Categories
                    .Where(c => c.UserId == userId && c.Type == CategoryType.Expense)
                    .OrderBy(c => c.Name)
                    .Select(c => new { c.Id, c.Name })
                    .ToListAsync();
                return View(expense);
            }

            _context.Expenses.Add(expense);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = "First expense saved. Welcome aboard!";
            return RedirectToAction(nameof(Done));
        }

        // Step 3 — done; nudge toward Dashboard
        public IActionResult Done()
        {
            return View();
        }
    }
}
