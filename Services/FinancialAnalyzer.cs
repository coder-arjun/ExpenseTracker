using ExpenseTracker.Data;
using ExpenseTracker.Models.Domain;
using Microsoft.EntityFrameworkCore;

namespace ExpenseTracker.Services
{
    /// <summary>
    /// Computes a <see cref="FinancialSnapshot"/> for one user / one yyyy-MM period.
    /// Pure C#, runs against the local DB — no data leaves the machine.
    /// </summary>
    public class FinancialAnalyzer
    {
        private readonly ApplicationDbContext _db;

        // ── Tuning knobs ────────────────────────────────────────────────
        // Anomaly detection: a category fires if current > expected × ratio
        // AND the excess clears a floor AND the trailing avg is meaningful.
        private const decimal AnomalyRatio = 1.75m;          // tighter than 1.5 — fewer "just elevated" hits
        private const decimal AnomalyMinExcess = 500m;       // floor for low-income users
        private const decimal AnomalyMinTrailingAvg = 500m;  // skip categories with no real history
        private const decimal AnomalyIncomeFraction = 0.005m;// 0.5% of income — scales excess floor up for high earners

        // Recurring detection: same Description across N+ trailing months.
        private const decimal RecurringAmountTolerance = 0.12m; // ±12% — EMIs and subs are very steady
        private const int RecurringMinOccurrences = 3;
        private const int RecurringMaxPerMonth = 2;          // > this in any one month = a habit, not a sub
        private const decimal RecurringMinAmount = 100m;     // catch cheap subs like Spotify (₹119), YouTube (₹149)

        // Opportunity floor: don't list trivial "savings".
        private const decimal OpportunityMinSavingFloor = 500m;
        private const decimal OpportunityIncomeFraction = 0.005m; // same scaling as anomaly excess

        public FinancialAnalyzer(ApplicationDbContext db)
        {
            _db = db;
        }

        /// <param name="asOf">
        /// When supplied (and within the period), the snapshot is "month-to-date":
        /// only days 1..asOf are summed for the current-period figures, the MoM
        /// comparison uses the same day-range in the prior month, and the anomaly
        /// detector prorates the trailing-3-month average by elapsed days.
        /// </param>
        public async Task<FinancialSnapshot> BuildSnapshotAsync(string userId, string period, DateTime? asOf = null)
        {
            var (start, end) = MonthRange(period);
            var isMtd = asOf.HasValue && asOf.Value.Date >= start && asOf.Value.Date < end;
            var cutoff = isMtd ? asOf!.Value.Date : end.AddDays(-1);
            var daysInMonth = DateTime.DaysInMonth(start.Year, start.Month);
            var daysElapsed = isMtd ? cutoff.Day : daysInMonth;
            // Proration factor: 1.0 for full month, ~0.5 mid-month, etc.
            // Used to scale full-month trailing averages down for fair MTD comparison.
            var prorate = (decimal)daysElapsed / daysInMonth;

            var snap = new FinancialSnapshot
            {
                Period = period,
                AsOf = isMtd ? cutoff : (DateTime?)null
            };

            // ---- 1. Totals --------------------------------------------------
            var incomeQuery = _db.Incomes.Where(i => i.UserId == userId && i.YearMonth == period);
            var expenseQuery = _db.Expenses.Where(e => e.UserId == userId && e.Month == period);
            if (isMtd)
            {
                incomeQuery = incomeQuery.Where(i => i.Date <= cutoff);
                expenseQuery = expenseQuery.Where(e => e.Date <= cutoff);
            }

            snap.TotalIncome = await incomeQuery.SumAsync(i => (decimal?)i.Amount) ?? 0;
            snap.TotalExpenses = await expenseQuery.SumAsync(e => (decimal?)e.Amount) ?? 0;
            // Read-out only — the count and the elapsed-day span the view needs to
            // show "N transactions" and a daily average. Nothing below reads them.
            snap.ExpenseCount = await expenseQuery.CountAsync();
            snap.DaysElapsed = daysElapsed;

            snap.SavingsRate = snap.TotalIncome > 0
                ? Math.Round((snap.TotalIncome - snap.TotalExpenses) / snap.TotalIncome, 4)
                : 0;

            // ---- 2. Per-category spend + MoM change -------------------------
            // For MTD, the prior-month comparison must use the SAME day-of-month
            // cutoff, otherwise we'd compare 15 days vs 30 days.
            var thisMonthFiltered = _db.Expenses
                .Include(e => e.Category)
                .Where(e => e.UserId == userId && e.Month == period);
            if (isMtd) thisMonthFiltered = thisMonthFiltered.Where(e => e.Date <= cutoff);

            var thisMonth = await thisMonthFiltered
                .GroupBy(e => new { e.CategoryId, Name = e.Category!.Name })
                .Select(g => new { g.Key.CategoryId, g.Key.Name, Total = g.Sum(e => e.Amount) })
                .ToListAsync();

            var priorPeriod = ShiftMonth(period, -1);
            var priorQuery = _db.Expenses.Where(e => e.UserId == userId && e.Month == priorPeriod);
            if (isMtd) priorQuery = priorQuery.Where(e => e.Date.Day <= daysElapsed);

            var prior = await priorQuery
                .GroupBy(e => e.CategoryId)
                .Select(g => new { g.Key, Total = g.Sum(e => e.Amount) })
                .ToDictionaryAsync(g => g.Key, g => g.Total);

            foreach (var c in thisMonth.OrderByDescending(x => x.Total))
            {
                decimal? mom = null;
                if (prior.TryGetValue(c.CategoryId, out var prev) && prev > 0)
                    mom = Math.Round((c.Total - prev) / prev * 100m, 1);

                snap.Categories.Add(new CategorySpend
                {
                    Category = c.Name ?? "Uncategorised",
                    Amount = c.Total,
                    PctOfSpend = snap.TotalExpenses > 0
                        ? Math.Round(c.Total / snap.TotalExpenses * 100m, 1)
                        : 0,
                    MoMChangePct = mom
                });
            }

            // ---- 3. Trailing 3-month averages (for anomalies + budget hints) -
            var threeMonthsAgo = ShiftMonth(period, -3);
            var twoMonthsAgo = ShiftMonth(period, -2);
            var oneMonthAgo = priorPeriod;
            var trailingMonths = new[] { threeMonthsAgo, twoMonthsAgo, oneMonthAgo };

            var trailing = await _db.Expenses
                .Include(e => e.Category)
                .Where(e => e.UserId == userId && trailingMonths.Contains(e.Month))
                .GroupBy(e => new { e.Month, CategoryName = e.Category!.Name })
                .Select(g => new { g.Key.Month, g.Key.CategoryName, Total = g.Sum(e => e.Amount) })
                .ToListAsync();

            // Average across the 3 months. Missing-month for a category counts as 0
            // — we want the average user behaviour, not the average of months they spent.
            var trailingAvg = trailing
                .GroupBy(t => t.CategoryName ?? "Uncategorised")
                .ToDictionary(
                    g => g.Key,
                    g => Math.Round(g.Sum(t => t.Total) / 3m, 2));

            snap.TrailingAverages = trailingAvg;

            // ---- 4. Anomalies (current > expected × ratio) ------------------
            // For MTD, compare against the prorated trailing average so 15 days
            // of spend isn't unfairly compared to a 30-day baseline.
            // The excess floor scales with income: trivial-looking anomalies
            // matter more for low earners, less for high earners.
            var excessFloor = Math.Max(AnomalyMinExcess, snap.TotalIncome * AnomalyIncomeFraction);

            foreach (var c in snap.Categories)
            {
                if (!trailingAvg.TryGetValue(c.Category, out var fullMonthAvg) || fullMonthAvg < AnomalyMinTrailingAvg)
                    continue;

                var expected = fullMonthAvg * prorate;
                var excess = c.Amount - expected;
                if (c.Amount > expected * AnomalyRatio && excess >= excessFloor)
                {
                    var note = isMtd
                        ? $"Spent ₹{c.Amount:N0} in the first {daysElapsed} days vs an expected ₹{expected:N0} at this point in the month."
                        : $"Spent ₹{c.Amount:N0} this month vs a 3-month average of ₹{fullMonthAvg:N0}.";

                    snap.Anomalies.Add(new Anomaly
                    {
                        Category = c.Category,
                        Amount = Math.Round(excess, 2),
                        Note = note
                    });
                }
            }

            // ---- 5. Recurring charges (~monthly cadence over 3+ months) ----
            // Heuristic: same Description (case-insensitive) appears in ≥ N
            // distinct months out of the trailing window, with amounts within
            // ±tolerance of the median.
            // For MTD: exclude the current partial month from detection (a single
            // mid-month occurrence shouldn't tip a 3-month threshold), but use the
            // full trailing 3 months as the window — those are complete.
            var recurringWindow = isMtd
                ? new[] { threeMonthsAgo, twoMonthsAgo, oneMonthAgo }
                : new[] { threeMonthsAgo, twoMonthsAgo, oneMonthAgo, period };
            var candidates = await _db.Expenses
                .Where(e => e.UserId == userId
                         && recurringWindow.Contains(e.Month)
                         && e.Description != null
                         && e.Description != "")
                .Select(e => new { e.Description, e.Amount, e.Date, e.Month })
                .ToListAsync();

            var grouped = candidates
                .GroupBy(c => (c.Description ?? "").Trim().ToLowerInvariant())
                .Where(g => g.Select(x => x.Month).Distinct().Count() >= RecurringMinOccurrences);

            foreach (var g in grouped)
            {
                // Filter out "daily/weekly habit" descriptions — if the description
                // shows up MANY times in any single month, it's a purchase pattern
                // (tea, juice, fuel), not a true monthly subscription.
                var maxPerMonth = g.GroupBy(x => x.Month).Max(m => m.Count());
                if (maxPerMonth > RecurringMaxPerMonth) continue;

                var amounts = g.Select(x => x.Amount).OrderBy(x => x).ToList();
                var median = amounts[amounts.Count / 2];

                // Ignore noise below the floor (true subs are typically ≥ ₹200/month).
                if (median < RecurringMinAmount) continue;

                // Require the amount to be steady across occurrences.
                var withinTolerance = amounts.Count(a =>
                    Math.Abs(a - median) <= median * RecurringAmountTolerance);
                if (withinTolerance < RecurringMinOccurrences) continue;

                var sample = g.First();
                snap.RecurringCharges.Add(new RecurringCharge
                {
                    Merchant = ToTitleCase(sample.Description ?? ""),
                    Amount = median,
                    Frequency = "monthly",
                    LastSeen = g.Max(x => x.Date)
                });
            }

            snap.RecurringCharges = snap.RecurringCharges
                .OrderByDescending(r => r.Amount)
                .ToList();

            // ---- 6. Budgets vs actuals -------------------------------------
            var budgets = await _db.Budgets
                .Include(b => b.Category)
                .Where(b => b.UserId == userId && b.YearMonth == period)
                .ToListAsync();

            var thisMonthByName = thisMonth.ToDictionary(t => t.Name ?? "Uncategorised", t => t.Total);

            foreach (var b in budgets)
            {
                var name = b.Category?.Name ?? "Overall";
                // For MTD, "actual" is the spend so far; variancePct measures spend
                // against full-month budget — so it will read as negative early in
                // the month even when on-track. That's what we want — the renderer
                // can frame it as "X% used so far".
                var actual = b.CategoryId.HasValue
                    ? (thisMonthByName.TryGetValue(name, out var v) ? v : 0)
                    : snap.TotalExpenses;
                var variance = b.Amount > 0
                    ? Math.Round((actual - b.Amount) / b.Amount * 100m, 1)
                    : 0;

                snap.Budgets.Add(new BudgetLine
                {
                    Category = name,
                    Budget = b.Amount,
                    Actual = actual,
                    VariancePct = variance
                });
            }

            // ---- 7. Opportunities (ranked) ---------------------------------
            // Each recurring charge → "subscription" (full amount recoverable).
            foreach (var r in snap.RecurringCharges)
            {
                snap.Opportunities.Add(new Opportunity
                {
                    Type = "subscription",
                    Label = $"{r.Merchant} recurring charge",
                    MonthlySaving = r.Amount,
                    Difficulty = r.Amount < 500 ? "easy" : "medium",
                    Evidence = $"Detected ~monthly charge of ₹{r.Amount:N0} (last seen {r.LastSeen:yyyy-MM-dd})."
                });
            }

            // Each anomaly → "spike" (excess recoverable).
            // Label is the NOUN PHRASE only; the renderer prepends an action verb.
            foreach (var a in snap.Anomalies)
            {
                snap.Opportunities.Add(new Opportunity
                {
                    Type = "spike",
                    Label = $"{a.Category} spike",
                    MonthlySaving = a.Amount,
                    Difficulty = "medium",
                    Evidence = a.Note
                });
            }

            // Each over-budget line → "overspend" (variance recoverable).
            foreach (var b in snap.Budgets.Where(b => b.Actual > b.Budget))
            {
                var over = b.Actual - b.Budget;
                snap.Opportunities.Add(new Opportunity
                {
                    Type = "overspend",
                    Label = $"{b.Category} budget overrun",
                    MonthlySaving = Math.Round(over, 2),
                    Difficulty = b.VariancePct > 30 ? "hard" : "medium",
                    Evidence = $"Spent ₹{b.Actual:N0} against a budget of ₹{b.Budget:N0} ({b.VariancePct:+0.0;-0.0}%)."
                });
            }

            // Drop trivial opportunities (would be weird to "suggest saving ₹40").
            // Floor scales with income — for ₹50k earners the ₹500 floor holds;
            // for ₹5L earners it lifts to ₹2,500 so the report stays focused.
            var oppFloor = Math.Max(OpportunityMinSavingFloor, snap.TotalIncome * OpportunityIncomeFraction);

            // Then de-dupe (a category might appear as both anomaly and overspend);
            // keep the higher-saving entry.
            snap.Opportunities = snap.Opportunities
                .Where(o => o.MonthlySaving >= oppFloor)
                .GroupBy(o => (o.Type, o.Label))
                .Select(g => g.OrderByDescending(o => o.MonthlySaving).First())
                .OrderByDescending(o => o.MonthlySaving)
                .Take(5)
                .ToList();

            return snap;
        }

        // yyyy-MM → (firstOfMonth, firstOfNextMonth)
        private static (DateTime start, DateTime end) MonthRange(string period)
        {
            var d = DateTime.ParseExact(period, "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
            return (d, d.AddMonths(1));
        }

        private static string ShiftMonth(string period, int delta)
        {
            var d = DateTime.ParseExact(period, "yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);
            return d.AddMonths(delta).ToString("yyyy-MM");
        }

        private static string ToTitleCase(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return s;
            return System.Globalization.CultureInfo.CurrentCulture.TextInfo
                .ToTitleCase(s.Trim().ToLowerInvariant());
        }
    }
}
