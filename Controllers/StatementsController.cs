using ExpenseTracker.Models.Domain;
using ExpenseTracker.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;

namespace ExpenseTracker.Controllers
{
    [Authorize]
    public class StatementsController : Controller
    {
        private readonly StatementService _svc;
        private readonly UserManager<ApplicationUser> _users;
        private readonly IConfiguration _config;
        private readonly ILogger<StatementsController> _log;

        public StatementsController(StatementService svc, UserManager<ApplicationUser> users,
            IConfiguration config, ILogger<StatementsController> log)
        {
            _svc = svc; _users = users; _config = config; _log = log;
        }

        // POST /Statements/Email — email the signed-in user their statement for {period}.
        [HttpPost]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Email(string? period)
        {
            var userId = _users.GetUserId(User)!;
            DateTime? asOf = null;
            if (string.IsNullOrWhiteSpace(period) || period == "mtd")
            {
                period = DateTime.Now.ToString("yyyy-MM");
                asOf = DateTime.Now;        // month-to-date
            }
            var backPeriod = asOf.HasValue ? "mtd" : period;

            if (!_svc.EmailConfigured)
            {
                TempData["ErrorMessage"] = "Email isn't set up on the server yet, so the statement couldn't be sent.";
                return RedirectToAction("Index", "Insights", new { period = backPeriod });
            }

            try
            {
                var (sent, message) = await _svc.EmailToUserAsync(userId, period, asOf);
                TempData[sent ? "SuccessMessage" : "ErrorMessage"] = sent ? "📧 " + message : message;
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "On-demand statement email failed for {User}", userId);
                TempData["ErrorMessage"] = "Sorry — something went wrong sending your statement. Please try again.";
            }
            return RedirectToAction("Index", "Insights", new { period = backPeriod });
        }

        // GET /Statements/RunMonthly?key=SECRET[&period=yyyy-MM]
        // Called by an external cron (e.g. cron-job.org) on the 1st of each month.
        // Anonymous, but gated by a secret key from config (Statements:CronKey).
        [AllowAnonymous]
        [HttpGet]
        public async Task<IActionResult> RunMonthly(string? key, string? period)
        {
            var expected = _config["Statements:CronKey"];
            if (string.IsNullOrWhiteSpace(expected) || key != expected)
                return Unauthorized("Invalid or missing key.");

            period ??= DateTime.Now.AddMonths(-1).ToString("yyyy-MM");   // last completed month
            if (!_svc.EmailConfigured)
                return Content($"Email not configured; nothing sent for {period}.", "text/plain");

            var (sent, skipped, failed) = await _svc.RunMonthlyAsync(period);
            _log.LogInformation("Cron RunMonthly {Period}: sent={Sent} skipped={Skipped} failed={Failed}", period, sent, skipped, failed);
            return Content($"Finoma monthly statements for {period}: sent={sent}, already-sent={skipped}, failed={failed}.", "text/plain");
        }
    }
}
