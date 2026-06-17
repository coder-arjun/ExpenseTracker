using ExpenseTracker.Data;
using ExpenseTracker.Models.Domain;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Controllers
{
    [Authorize]
    public class GoalsController : Controller
    {
        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;

        public GoalsController(ApplicationDbContext context, UserManager<ApplicationUser> userManager)
        {
            _context = context;
            _userManager = userManager;
        }

        public async Task<IActionResult> Index(GoalStatus? status = null)
        {
            var userId = _userManager.GetUserId(User);
            var query = _context.Goals.Where(g => g.UserId == userId);
            if (status.HasValue) query = query.Where(g => g.Status == status.Value);

            var goals = await query
                .OrderByDescending(g => g.Status == GoalStatus.Active)
                .ThenBy(g => g.Deadline ?? DateTime.MaxValue)
                .ToListAsync();

            // Compute progress for each: sum of contributions.
            var ids = goals.Select(g => g.Id).ToList();
            var totals = await _context.GoalContributions
                .Where(c => ids.Contains(c.GoalId))
                .GroupBy(c => c.GoalId)
                .Select(g => new { GoalId = g.Key, Total = g.Sum(c => c.Amount) })
                .ToDictionaryAsync(x => x.GoalId, x => x.Total);

            ViewData["Progress"] = totals;
            ViewData["StatusFilter"] = status;
            return View(goals);
        }

        public IActionResult Create()
        {
            return View(new Goal { Status = GoalStatus.Active });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Create([Bind("Name,TargetAmount,Deadline,Description")] Goal goal)
        {
            var userId = _userManager.GetUserId(User)!;
            goal.UserId = userId;
            goal.Status = GoalStatus.Active;
            ClearServerFieldErrors();

            if (!ModelState.IsValid) return View(goal);

            if (goal.TargetAmount <= 0)
            {
                ModelState.AddModelError(nameof(goal.TargetAmount), "Target must be greater than zero.");
                return View(goal);
            }

            _context.Add(goal);
            await _context.SaveChangesAsync();
            TempData["SuccessMessage"] = $"Goal '{goal.Name}' created. You've got this!";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Edit(int? id)
        {
            if (id == null) return NotFound();
            var userId = _userManager.GetUserId(User);
            var goal = await _context.Goals.FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId);
            if (goal == null) return NotFound();
            return View(goal);
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Edit(int id, [Bind("Id,Name,TargetAmount,Deadline,Description,Status")] Goal goal)
        {
            if (id != goal.Id) return NotFound();
            var userId = _userManager.GetUserId(User);
            var existing = await _context.Goals.FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId);
            if (existing == null) return NotFound();

            ClearServerFieldErrors();
            if (!ModelState.IsValid) return View(goal);

            existing.Name = goal.Name;
            existing.TargetAmount = goal.TargetAmount;
            existing.Deadline = goal.Deadline;
            existing.Description = goal.Description;
            existing.Status = goal.Status;
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Goal '{existing.Name}' updated.";
            return RedirectToAction(nameof(Index));
        }

        public async Task<IActionResult> Delete(int? id)
        {
            if (id == null) return NotFound();
            var userId = _userManager.GetUserId(User);
            var goal = await _context.Goals.FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId);
            if (goal == null) return NotFound();
            return View(goal);
        }

        [HttpPost, ActionName("Delete")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> DeleteConfirmed(int id)
        {
            var userId = _userManager.GetUserId(User);
            var goal = await _context.Goals.FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId);
            if (goal != null)
            {
                _context.Goals.Remove(goal);
                await _context.SaveChangesAsync();
                TempData["SuccessMessage"] = "Goal deleted.";
            }
            return RedirectToAction(nameof(Index));
        }

        // Contribute: add to a goal's running total.
        public async Task<IActionResult> Contribute(int id)
        {
            var userId = _userManager.GetUserId(User);
            var goal = await _context.Goals.FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId);
            if (goal == null) return NotFound();

            var total = await _context.GoalContributions.Where(c => c.GoalId == id).SumAsync(c => (decimal?)c.Amount) ?? 0;
            ViewData["CurrentTotal"] = total;
            return View(new GoalContribution
            {
                GoalId = id,
                Date = DateTime.Today,
                Amount = 0
            });
        }

        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Contribute(int id, [Bind("Amount,Date,Note")] GoalContribution contribution)
        {
            var userId = _userManager.GetUserId(User)!;
            var goal = await _context.Goals.FirstOrDefaultAsync(g => g.Id == id && g.UserId == userId);
            if (goal == null) return NotFound();

            if (contribution.Amount <= 0)
                ModelState.AddModelError(nameof(contribution.Amount), "Amount must be greater than zero.");

            ModelState.Remove(nameof(GoalContribution.UserId));
            ModelState.Remove(nameof(GoalContribution.User));
            ModelState.Remove(nameof(GoalContribution.Goal));

            if (!ModelState.IsValid)
            {
                var t = await _context.GoalContributions.Where(c => c.GoalId == id).SumAsync(c => (decimal?)c.Amount) ?? 0;
                ViewData["CurrentTotal"] = t;
                contribution.GoalId = id;
                return View(contribution);
            }

            contribution.GoalId = id;
            contribution.UserId = userId;
            _context.GoalContributions.Add(contribution);

            // Auto-flip to Completed when target hit.
            var newTotal = (await _context.GoalContributions.Where(c => c.GoalId == id).SumAsync(c => (decimal?)c.Amount) ?? 0)
                            + contribution.Amount;
            if (newTotal >= goal.TargetAmount && goal.Status == GoalStatus.Active)
            {
                goal.Status = GoalStatus.Completed;
                TempData["SuccessMessage"] = $"🎉 You hit your '{goal.Name}' goal! Marked as completed.";
            }
            else
            {
                TempData["SuccessMessage"] = "Contribution added.";
            }

            await _context.SaveChangesAsync();
            return RedirectToAction(nameof(Index));
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
