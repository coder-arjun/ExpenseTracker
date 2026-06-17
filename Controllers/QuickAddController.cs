using ExpenseTracker.Data;
using ExpenseTracker.Models.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Controllers
{
    /// <summary>
    /// Tiny endpoints powering the floating Quick-Add sheet. JSON in/out; XHR-only.
    /// Anti-forgery is enforced via the RequestVerificationToken header that
    /// quick-add.js reads from the page's hidden input.
    /// </summary>
    [Authorize]
    [Route("[controller]/[action]")]
    public class QuickAddController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public QuickAddController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        // GET /QuickAdd/Categories  →  [{ id, name }] for this user's Expense categories
        [HttpGet]
        public async Task<IActionResult> Categories()
        {
            var userId = _userManager.GetUserId(User);
            var cats = await _context.Categories
                .Where(c => c.UserId == userId && c.Type == CategoryType.Expense)
                .OrderBy(c => c.Name)
                .Select(c => new { id = c.Id, name = c.Name })
                .ToListAsync();
            return Json(cats);
        }

        public class QuickExpenseDto
        {
            public decimal Amount { get; set; }
            public string? Description { get; set; }
            public int CategoryId { get; set; }
        }

        // POST /QuickAdd/Expense
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Expense([FromBody] QuickExpenseDto dto)
        {
            if (dto == null || dto.Amount <= 0 || dto.CategoryId <= 0)
                return BadRequest("Amount and category are required.");

            var userId = _userManager.GetUserId(User)!;

            // Verify the category belongs to this user and is an Expense category.
            var ok = await _context.Categories.AnyAsync(c =>
                c.Id == dto.CategoryId && c.UserId == userId && c.Type == CategoryType.Expense);
            if (!ok) return BadRequest("Invalid category.");

            var today = DateTime.Today;
            var expense = new Expense
            {
                Amount = dto.Amount,
                Date = today,
                Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
                CategoryId = dto.CategoryId,
                Month = today.ToString("yyyy-MM"),
                UserId = userId,
            };

            _context.Expenses.Add(expense);
            await _context.SaveChangesAsync();
            return Ok(new { id = expense.Id });
        }
    }
}
