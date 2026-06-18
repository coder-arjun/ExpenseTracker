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
    public class InsightsController : Controller
    {
        // Sentinel value used in the period dropdown to mean "month-to-date".
        // The Generate action expands this to the current yyyy-MM + asOf=today.
        public const string MonthToDateKey = "mtd";

        private readonly ApplicationDbContext _context;
        private readonly UserManager<ApplicationUser> _userManager;
        private readonly FinancialAnalyzer _analyzer;

        public InsightsController(
            ApplicationDbContext context,
            UserManager<ApplicationUser> userManager,
            FinancialAnalyzer analyzer)
        {
            _context = context;
            _userManager = userManager;
            _analyzer = analyzer;
        }

        // GET /Insights?period=2026-05  or  /Insights?period=mtd
        // Default = last completed month.
        public async Task<IActionResult> Index(string? period = null)
        {
            var userId = _userManager.GetUserId(User)!;
            period ??= LastCompletedMonth();

            ViewData["Period"] = period;
            ViewData["PeriodOptions"] = BuildPeriodOptions(period);

            string? markdown = null;
            DateTime? generatedAt = null;
            FinancialSnapshot? snapshot = null;

            if (period == MonthToDateKey)
            {
                // MTD is never cached (changes daily). Compute on the fly.
                snapshot = await _analyzer.BuildSnapshotAsync(userId, CurrentMonth(), DateTime.Now);
                markdown = InsightsRenderer.Render(snapshot);
                generatedAt = DateTime.UtcNow;
                ViewData["IsMonthToDate"] = true;
            }
            else
            {
                var cached = await _context.MonthlyInsights
                    .FirstOrDefaultAsync(m => m.UserId == userId && m.Period == period);
                markdown = cached?.MarkdownContent;
                generatedAt = cached?.GeneratedAt;
                ViewData["IsMonthToDate"] = false;

                // When an insight has been generated for this month, also recompute the
                // structured snapshot so the view can render charts/gauges/progress bars
                // alongside the cached narrative. The figures are deterministic from the
                // ledger, so this matches what was cached. Skipped when nothing's generated
                // yet (we show the empty state instead).
                if (cached != null)
                    snapshot = await _analyzer.BuildSnapshotAsync(userId, period);
            }

            ViewData["GeneratedAt"] = generatedAt;
            ViewData["Markdown"] = markdown;
            return View(snapshot);
        }

        // POST /Insights/Generate
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Generate(string period)
        {
            var userId = _userManager.GetUserId(User)!;
            if (string.IsNullOrWhiteSpace(period))
            {
                TempData["ErrorMessage"] = "Please pick a period.";
                return RedirectToAction(nameof(Index));
            }

            // Month-to-date: don't persist (it changes every day). Just bounce back
            // to Index, which will recompute fresh.
            if (period == MonthToDateKey)
                return RedirectToAction(nameof(Index), new { period });

            var snapshot = await _analyzer.BuildSnapshotAsync(userId, period);
            var markdown = InsightsRenderer.Render(snapshot);

            var existing = await _context.MonthlyInsights
                .FirstOrDefaultAsync(m => m.UserId == userId && m.Period == period);
            if (existing == null)
            {
                _context.MonthlyInsights.Add(new MonthlyInsight
                {
                    UserId = userId,
                    Period = period,
                    MarkdownContent = markdown,
                    GeneratedAt = DateTime.UtcNow
                });
            }
            else
            {
                existing.MarkdownContent = markdown;
                existing.GeneratedAt = DateTime.UtcNow;
            }
            await _context.SaveChangesAsync();

            TempData["SuccessMessage"] = $"Insights generated for {period}.";
            return RedirectToAction(nameof(Index), new { period });
        }

        private static string LastCompletedMonth()
            => DateTime.Now.AddMonths(-1).ToString("yyyy-MM");

        private static string CurrentMonth()
            => DateTime.Now.ToString("yyyy-MM");

        // Returns the dropdown options: "mtd" first, then up to 12 previous months.
        // We return string keys; the view labels them.
        private static List<string> BuildPeriodOptions(string current)
        {
            var list = new List<string> { MonthToDateKey };
            var now = DateTime.Now;
            for (int i = 1; i <= 12; i++)
                list.Add(now.AddMonths(-i).ToString("yyyy-MM"));
            if (!list.Contains(current))
                list.Insert(1, current);
            return list;
        }
    }
}
