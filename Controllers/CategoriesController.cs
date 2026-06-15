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
    public class CategoriesController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public CategoriesController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET: Categories
        public async Task<IActionResult> Index(int page = 1, CategoryType? type = null)
        {
            var userId = _userManager.GetUserId(User);
            var query = _context.Categories.Where(c => c.UserId == userId);

            if (type.HasValue)
                query = query.Where(c => c.Type == type.Value);

            query = query.OrderBy(c => c.Type).ThenBy(c => c.Name);

            ViewData["TypeFilter"] = type;
            return View(await PaginatedList<Category>.CreateAsync(query, page));
        }

        // GET: Categories/Create
        public IActionResult Create(CategoryType? type = null)
        {
            return View(new Category { Type = type ?? CategoryType.Expense });
        }

        // POST: Categories/Create
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Name,Type")] Category category)
        {
            var userId = _userManager.GetUserId(User)!;
            category.UserId = userId;
            category.Name = (category.Name ?? "").Trim();
            ClearServerFieldErrors();

            if (ModelState.IsValid)
            {
                var exists = await _context.Categories.AnyAsync(c =>
                    c.UserId == userId && c.Type == category.Type && c.Name == category.Name);
                if (exists)
                {
                    ModelState.AddModelError(nameof(category.Name),
                        $"A {category.Type} category named '{category.Name}' already exists.");
                }
                else
                {
                    _context.Add(category);
                    await _context.SaveChangesAsync();
                    TempData["SuccessMessage"] = $"{category.Type} category '{category.Name}' created.";
                    return RedirectToAction(nameof(Index), new { type = category.Type });
                }
            }

            TempData["ErrorMessage"] = string.Join(" ", ModelState.Values
                .SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
            return View(category);
        }

        // GET: Categories/Edit/5
        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var userId = _userManager.GetUserId(User);
            var cat = await _context.Categories.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
            if (cat == null) return NotFound();
            return View(cat);
        }

        // POST: Categories/Edit/5
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name,Type")] Category category)
        {
            if (id != category.Id) return NotFound();
            var userId = _userManager.GetUserId(User)!;

            var existing = await _context.Categories.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
            if (existing == null) return NotFound();

            category.Name = (category.Name ?? "").Trim();
            ModelState.Remove(nameof(Category.UserId));
            ModelState.Remove(nameof(Category.User));

            if (!ModelState.IsValid)
            {
                TempData["ErrorMessage"] = string.Join(" ", ModelState.Values
                    .SelectMany(v => v.Errors).Select(e => e.ErrorMessage));
                return View(category);
            }

            // Uniqueness check excluding the current row
            var conflict = await _context.Categories.AnyAsync(c =>
                c.UserId == userId && c.Type == category.Type && c.Name == category.Name && c.Id != id);
            if (conflict)
            {
                ModelState.AddModelError(nameof(category.Name),
                    $"A {category.Type} category named '{category.Name}' already exists.");
                return View(category);
            }

            existing.Name = category.Name;
            existing.Type = category.Type;
            try
            {
                await _context.SaveChangesAsync();
            }
            catch (DbUpdateConcurrencyException)
            {
                if (!_context.Categories.Any(c => c.Id == id)) return NotFound();
                throw;
            }

            TempData["SuccessMessage"] = $"Category '{existing.Name}' updated.";
            return RedirectToAction(nameof(Index), new { type = existing.Type });
        }

        // GET: Categories/Delete/5
        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();
            var userId = _userManager.GetUserId(User);
            var cat = await _context.Categories.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
            if (cat == null) return NotFound();

            // Surface usage so user knows what would block deletion
            ViewData["UsageCount"] = await CountUsageAsync(cat);
            return View(cat);
        }

        // POST: Categories/Delete/5
        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var userId = _userManager.GetUserId(User);
            var cat = await _context.Categories.FirstOrDefaultAsync(c => c.Id == id && c.UserId == userId);
            if (cat == null) return RedirectToAction(nameof(Index));

            var usage = await CountUsageAsync(cat);
            if (usage > 0)
            {
                TempData["ErrorMessage"] = $"Cannot delete '{cat.Name}' — it is used by {usage} record(s). Reassign or delete those first.";
                return RedirectToAction(nameof(Index), new { type = cat.Type });
            }

            var type = cat.Type;
            _context.Categories.Remove(cat);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Category '{cat.Name}' deleted.";
            return RedirectToAction(nameof(Index), new { type });
        }

        // Total references across transactions and budgets for this category.
        private async Task<int> CountUsageAsync(Category cat)
        {
            var expenses = cat.Type == CategoryType.Expense
                ? await _context.Expenses.CountAsync(e => e.CategoryId == cat.Id)
                : 0;
            var incomes = cat.Type == CategoryType.Income
                ? await _context.Incomes.CountAsync(i => i.CategoryId == cat.Id)
                : 0;
            var budgets = cat.Type == CategoryType.Expense
                ? await _context.Budgets.CountAsync(b => b.CategoryId == cat.Id)
                : 0;
            return expenses + incomes + budgets;
        }

        private void ClearServerFieldErrors()
        {
            var keysToRemove = ModelState.Keys
                .Where(k => k.Contains("UserId", StringComparison.OrdinalIgnoreCase)
                         || k.Contains("User", StringComparison.OrdinalIgnoreCase))
                .ToList();
            foreach (var key in keysToRemove) ModelState.Remove(key);
        }
    }
}
