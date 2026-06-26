using System.Globalization;
using ExpenseTracker.Data;
using ExpenseTracker.Models.Domain;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Services
{
    /// <summary>
    /// Builds the monthly statement (PDF) for a user and emails it. Also runs the
    /// month-end batch (one statement per user with activity), de-duplicated via
    /// <see cref="StatementDelivery"/> so the cron can be called more than once safely.
    /// </summary>
    public class StatementService
    {
        private readonly ApplicationDbContext _db;
        private readonly FinancialAnalyzer _analyzer;
        private readonly EmailSender _email;
        private readonly UserManager<ApplicationUser> _users;
        private readonly IConfiguration _config;
        private readonly ILogger<StatementService> _log;
        private static readonly CultureInfo INR = CultureInfo.GetCultureInfo("en-IN");

        public StatementService(ApplicationDbContext db, FinancialAnalyzer analyzer, EmailSender email,
            UserManager<ApplicationUser> users, IConfiguration config, ILogger<StatementService> log)
        {
            _db = db; _analyzer = analyzer; _email = email; _users = users; _config = config; _log = log;
        }

        public bool EmailConfigured => _email.IsConfigured;

        private string BaseUrl => (_config["App:BaseUrl"] ?? "https://finoma.runasp.net").TrimEnd('/');
        private static string M(decimal a) => a.ToString("C0", INR);

        // ── Assemble everything the PDF needs ──────────────────────────────
        public async Task<StatementData> GatherAsync(ApplicationUser user, string period, DateTime? asOf = null)
        {
            var snap = await _analyzer.BuildSnapshotAsync(user.Id, period, asOf);

            var incomeRows = await _db.Incomes
                .Where(i => i.UserId == user.Id && i.YearMonth == period && (asOf == null || i.Date <= asOf.Value))
                .Select(i => new { i.Amount, Cat = i.Category != null ? i.Category.Name : null })
                .ToListAsync();

            var totalIncome = snap.TotalIncome;
            var incomeByCat = incomeRows
                .GroupBy(r => r.Cat ?? "Uncategorised")
                .Select(g => new StatementCatLine(g.Key, g.Sum(x => x.Amount),
                    totalIncome > 0 ? g.Sum(x => x.Amount) / totalIncome * 100m : 0m))
                .OrderByDescending(l => l.Amount)
                .ToList();

            var expenseByCat = snap.Categories
                .Select(c => new StatementCatLine(c.Category, c.Amount, c.PctOfSpend, c.MoMChangePct))
                .ToList();

            var budgets = snap.Budgets
                .Select(b => new StatementBudgetLine(b.Category, b.Budget, b.Actual, b.VariancePct, b.Actual > b.Budget))
                .OrderByDescending(b => b.Over).ThenByDescending(b => b.Actual)
                .ToList();

            // Outstanding IOUs — a running balance, so we report the live position
            // (as of asOf/now) rather than a month-bound figure.
            var asOfDate = (asOf ?? DateTime.Now).Date;
            var debtRows = await _db.Debts
                .Where(x => x.UserId == user.Id && x.Amount - x.AmountPaid > 0m)
                .Select(x => new { x.PersonName, x.Direction, x.DueDate, Outstanding = x.Amount - x.AmountPaid })
                .ToListAsync();
            var openDebts = debtRows
                .OrderByDescending(x => x.Outstanding)
                .Take(15)
                .Select(x => new StatementDebtLine(
                    x.PersonName,
                    x.Direction == DebtDirection.TheyOweMe,
                    x.Outstanding,
                    x.DueDate.HasValue && x.DueDate.Value.Date < asOfDate,
                    x.DueDate))
                .ToList();
            var owedToMe = debtRows.Where(x => x.Direction == DebtDirection.TheyOweMe).Sum(x => x.Outstanding);
            var iOwe = debtRows.Where(x => x.Direction == DebtDirection.IOweThem).Sum(x => x.Outstanding);
            var debtOverdue = debtRows.Count(x => x.DueDate.HasValue && x.DueDate.Value.Date < asOfDate);

            var dt = DateTime.ParseExact(period, "yyyy-MM", CultureInfo.InvariantCulture);
            var label = dt.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            if (asOf.HasValue) label += $" (1–{asOf.Value.Day})";

            return new StatementData
            {
                DisplayName = string.IsNullOrWhiteSpace(user.DisplayUserId) ? (user.UserName ?? user.Email ?? "there") : user.DisplayUserId,
                Email = user.Email ?? "",
                Period = period,
                PeriodLabel = label,
                GeneratedAt = DateTime.Now,
                TotalIncome = snap.TotalIncome,
                TotalExpenses = snap.TotalExpenses,
                SavingsRatePct = (int)Math.Round(snap.SavingsRate * 100),
                IncomeByCategory = incomeByCat,
                ExpenseByCategory = expenseByCat,
                Budgets = budgets,
                Highlights = BuildHighlights(snap),
                OwedToMe = owedToMe,
                IOwe = iOwe,
                DebtOverdueCount = debtOverdue,
                OpenDebts = openDebts
            };
        }

        private static List<string> BuildHighlights(FinancialSnapshot s)
        {
            var hl = new List<string>();
            var net = s.TotalIncome - s.TotalExpenses;
            var pct = (int)Math.Round(s.SavingsRate * 100);

            if (s.TotalIncome == 0 && s.TotalExpenses == 0)
            {
                hl.Add("No income or expenses were recorded for this period.");
                return hl;
            }

            hl.Add(net >= 0
                ? $"You kept {M(net)} this month — about {pct}% of your income."
                : $"You spent {M(-net)} more than you earned this month — worth a closer look.");

            var top = s.Categories.FirstOrDefault();
            if (top != null)
                hl.Add($"Your largest expense was {top.Category} at {M(top.Amount)} ({top.PctOfSpend:0.0}% of spending).");

            foreach (var b in s.Budgets.Where(b => b.Actual > b.Budget).OrderByDescending(b => b.Actual - b.Budget).Take(2))
                hl.Add($"{b.Category} went {M(b.Actual - b.Budget)} over its {M(b.Budget)} budget.");

            var rising = s.Categories.FirstOrDefault(c => c.MoMChangePct.HasValue && c.MoMChangePct > 25 && c.Amount >= 1000);
            if (rising != null)
                hl.Add($"{rising.Category} rose {rising.MoMChangePct:+0.0;-0.0}% versus last month.");

            var opp = s.Opportunities.OrderByDescending(o => o.MonthlySaving).FirstOrDefault();
            if (opp != null)
                hl.Add($"Tip: reviewing {opp.Label} could free up about {M(opp.MonthlySaving)} a month.");

            return hl.Take(6).ToList();
        }

        // ── Email one user their statement ────────────────────────────────
        public async Task<(bool sent, string message)> EmailToUserAsync(string userId, string period, DateTime? asOf = null)
        {
            var user = await _users.FindByIdAsync(userId);
            if (user == null) return (false, "User not found.");
            if (string.IsNullOrWhiteSpace(user.Email)) return (false, "No email address on file for this account.");
            if (!_email.IsConfigured) return (false, "Email isn't configured on the server yet.");

            var data = await GatherAsync(user, period, asOf);
            var pdf = StatementPdf.Build(data);
            var subject = $"Your Finoma statement — {data.PeriodLabel}";
            await _email.SendAsync(user.Email!, data.DisplayName, subject, BuildEmailHtml(data), pdf, $"Finoma-Statement-{period}.pdf");

            _db.StatementDeliveries.Add(new StatementDelivery { UserId = userId, Period = period, SentAtUtc = DateTime.UtcNow, Success = true });
            await _db.SaveChangesAsync();
            return (true, $"Statement for {data.PeriodLabel} emailed to {user.Email}.");
        }

        // ── Month-end batch (one per user with activity, de-duplicated) ───
        public async Task<(int sent, int skipped, int failed)> RunMonthlyAsync(string period)
        {
            var expUsers = await _db.Expenses.Where(e => e.Month == period && e.UserId != null).Select(e => e.UserId!).Distinct().ToListAsync();
            var incUsers = await _db.Incomes.Where(i => i.YearMonth == period && i.UserId != null).Select(i => i.UserId!).Distinct().ToListAsync();
            var userIds = expUsers.Union(incUsers).Distinct().ToList();

            var already = await _db.StatementDeliveries.Where(s => s.Period == period && s.Success).Select(s => s.UserId).Distinct().ToListAsync();
            var todo = userIds.Except(already).ToList();

            int sent = 0, failed = 0;
            foreach (var uid in todo)
            {
                try
                {
                    var (ok, msg) = await EmailToUserAsync(uid, period);
                    if (ok) sent++; else { failed++; _log.LogWarning("Statement skipped for {User}: {Msg}", uid, msg); }
                }
                catch (Exception ex)
                {
                    failed++;
                    _log.LogError(ex, "Statement send failed for {User}", uid);
                    _db.StatementDeliveries.Add(new StatementDelivery { UserId = uid, Period = period, SentAtUtc = DateTime.UtcNow, Success = false });
                    await _db.SaveChangesAsync();
                }
            }
            _log.LogInformation("Monthly statements for {Period}: {Sent} sent, {Skipped} already-sent, {Failed} failed.", period, sent, already.Count, failed);
            return (sent, already.Count, failed);
        }

        // ── Branded HTML email body (inline styles for mail clients) ──────
        private string BuildEmailHtml(StatementData d)
        {
            string net = M(Math.Abs(d.NetSaved));
            string netLabel = d.NetSaved >= 0 ? "Net saved" : "Shortfall";
            string debtBlock = d.HasDebts ? $@"
      <div style=""margin:0 0 18px;padding:14px 16px;background:#FAF9F5;border:1px solid #E7E2D7;border-radius:8px;"">
        <div style=""font-size:12px;letter-spacing:1px;color:#8A857A;margin-bottom:8px;"">MONEY OWED (OUTSTANDING)</div>
        <table style=""width:100%;border-collapse:collapse;font-size:14px;"">
          <tr><td style=""padding:5px 0;color:#6b6459;"">Owed to you</td><td style=""padding:5px 0;text-align:right;color:#15715A;font-weight:bold;"">{M(d.OwedToMe)}</td></tr>
          <tr><td style=""padding:5px 0;color:#6b6459;"">You owe</td><td style=""padding:5px 0;text-align:right;color:#C20E3A;font-weight:bold;"">{M(d.IOwe)}</td></tr>
          <tr><td style=""padding:5px 0;color:#6b6459;border-top:1px solid #eee;"">Net</td><td style=""padding:5px 0;text-align:right;font-weight:bold;border-top:1px solid #eee;"">{(d.DebtNet >= 0 ? "+" : "−")}{M(Math.Abs(d.DebtNet))}{(d.DebtOverdueCount > 0 ? $" &middot; {d.DebtOverdueCount} overdue" : "")}</td></tr>
        </table>
      </div>" : "";
            string pdfContains = d.HasDebts
                ? "income, expenses, budgets, insights and outstanding IOUs"
                : "income, expenses, budgets and insights";
            return $@"
<div style=""background:#FAF9F5;padding:24px;font-family:Arial,Helvetica,sans-serif;color:#1A1814;"">
  <div style=""max-width:560px;margin:0 auto;background:#ffffff;border:1px solid #E7E2D7;border-radius:12px;overflow:hidden;"">
    <div style=""background:#C20E3A;padding:20px 26px;color:#ffffff;"">
      <div style=""font-size:12px;letter-spacing:3px;opacity:.85;"">FINOMA</div>
      <div style=""font-size:21px;font-weight:bold;margin-top:3px;"">Your Monthly Statement</div>
    </div>
    <div style=""padding:26px;"">
      <p style=""margin:0 0 14px;"">Hi {System.Net.WebUtility.HtmlEncode(d.DisplayName)},</p>
      <p style=""margin:0 0 18px;line-height:1.55;"">Here's your Finoma statement for <strong>{d.PeriodLabel}</strong>. The full breakdown — {pdfContains} — is attached as a PDF.</p>
      <table style=""width:100%;border-collapse:collapse;margin:0 0 20px;font-size:15px;"">
        <tr><td style=""padding:9px 0;color:#6b6459;"">Earned</td><td style=""padding:9px 0;text-align:right;color:#15715A;font-weight:bold;"">{M(d.TotalIncome)}</td></tr>
        <tr><td style=""padding:9px 0;color:#6b6459;border-top:1px solid #eee;"">Spent</td><td style=""padding:9px 0;text-align:right;color:#C20E3A;font-weight:bold;border-top:1px solid #eee;"">{M(d.TotalExpenses)}</td></tr>
        <tr><td style=""padding:9px 0;color:#6b6459;border-top:1px solid #eee;"">{netLabel}</td><td style=""padding:9px 0;text-align:right;font-weight:bold;border-top:1px solid #eee;"">{net} &middot; {d.SavingsRatePct}% rate</td></tr>
      </table>
      {debtBlock}
      <a href=""{BaseUrl}/Insights"" style=""display:inline-block;background:#1A1814;color:#ffffff;text-decoration:none;padding:12px 22px;border-radius:6px;font-weight:bold;"">View in Finoma &rarr;</a>
      <p style=""margin:22px 0 0;font-size:12px;color:#8A857A;line-height:1.5;"">You're receiving this because you have a Finoma account. This is general money-management guidance, not regulated financial advice.</p>
    </div>
  </div>
  <div style=""text-align:center;font-size:11px;color:#8A857A;margin-top:14px;"">finoma.runasp.net</div>
</div>";
        }
    }
}
