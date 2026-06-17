using System.Globalization;
using System.Text;

namespace ExpenseTracker.Services
{
    /// <summary>
    /// Turns a <see cref="FinancialSnapshot"/> into a friendly Markdown summary.
    /// Pure C# — no LLM call. Follows the section structure from the spec:
    ///   ## Overview
    ///   ## Where your money is leaking
    ///   ## How you can save
    ///   ## A budget to aim for
    ///   ## Do these first
    /// </summary>
    public static class InsightsRenderer
    {
        private static readonly CultureInfo INR = CultureInfo.GetCultureInfo("en-IN");

        public static string Render(FinancialSnapshot s)
        {
            var sb = new StringBuilder();

            WriteOverview(sb, s);
            WriteLeaks(sb, s);
            WriteSavings(sb, s);
            WriteBudgetTarget(sb, s);
            WriteDoFirst(sb, s);

            sb.AppendLine();
            sb.AppendLine("_This is general money-management guidance, not regulated financial or investment advice._");
            return sb.ToString();
        }

        // ─── ## Overview ────────────────────────────────────────────────────
        private static void WriteOverview(StringBuilder sb, FinancialSnapshot s)
        {
            sb.AppendLine("## Overview");
            sb.AppendLine();

            var net = s.TotalIncome - s.TotalExpenses;
            var savingsPct = (int)Math.Round(s.SavingsRate * 100);
            var periodLabel = PeriodLabel(s);

            if (s.TotalIncome == 0 && s.TotalExpenses == 0)
            {
                sb.AppendLine($"There's no income or expense data recorded for {periodLabel} yet, so there's nothing to analyse for this period.");
                sb.AppendLine();
                return;
            }

            var preposition = s.AsOf.HasValue ? "So far in" : "In";
            if (net >= 0)
            {
                sb.Append($"{preposition} {periodLabel} you earned {Money(s.TotalIncome)} and spent {Money(s.TotalExpenses)}, ");
                sb.Append($"keeping **{Money(net)}** ({savingsPct}% of your income).");
            }
            else
            {
                sb.Append($"{preposition} {periodLabel} you earned {Money(s.TotalIncome)} but spent {Money(s.TotalExpenses)}, ");
                sb.Append($"which is **{Money(-net)} more than you earned**.");
            }

            // For MTD, add a "X days in, Y to go" sentence so the user has context.
            if (s.AsOf.HasValue)
            {
                var daysIn = s.AsOf.Value.Day;
                var totalDays = DateTime.DaysInMonth(s.AsOf.Value.Year, s.AsOf.Value.Month);
                var daysLeft = totalDays - daysIn;
                sb.Append($" You're {daysIn} day{(daysIn == 1 ? "" : "s")} into the month with {daysLeft} to go.");
            }

            // The one thing to notice — pick the strongest signal.
            var headline = ChooseHeadline(s);
            if (!string.IsNullOrEmpty(headline))
                sb.Append(' ').Append(headline);

            sb.AppendLine();
            sb.AppendLine();
        }

        private static string ChooseHeadline(FinancialSnapshot s)
        {
            // Order of precedence: worst overspend > biggest anomaly > biggest rising category > top spend category.
            var worstOver = s.Budgets
                .Where(b => b.Actual > b.Budget)
                .OrderByDescending(b => b.Actual - b.Budget)
                .FirstOrDefault();
            if (worstOver != null)
                return $"The biggest thing to notice: **{worstOver.Category}** is over budget by {Money(worstOver.Actual - worstOver.Budget)}.";

            var biggestAnomaly = s.Anomalies.OrderByDescending(a => a.Amount).FirstOrDefault();
            if (biggestAnomaly != null)
                return $"The biggest thing to notice: **{biggestAnomaly.Category}** is well above its usual level this month.";

            var risingFast = s.Categories
                .Where(c => c.MoMChangePct.HasValue && c.MoMChangePct > 25 && c.Amount > 1000)
                .OrderByDescending(c => c.MoMChangePct)
                .FirstOrDefault();
            if (risingFast != null)
                return $"The biggest thing to notice: **{risingFast.Category}** rose {risingFast.MoMChangePct:+0.0;-0.0}% vs last month.";

            var topCat = s.Categories.FirstOrDefault();
            if (topCat != null)
                return $"Your largest spend was **{topCat.Category}** at {Money(topCat.Amount)} ({topCat.PctOfSpend:0.0}% of expenses).";

            return string.Empty;
        }

        // ─── ## Where your money is leaking ─────────────────────────────────
        private static void WriteLeaks(StringBuilder sb, FinancialSnapshot s)
        {
            sb.AppendLine("## Where your money is leaking");
            sb.AppendLine();

            var leaks = new List<(decimal impact, string line)>();

            // 1. Over-budget lines
            foreach (var b in s.Budgets.Where(b => b.Actual > b.Budget))
            {
                var over = b.Actual - b.Budget;
                leaks.Add((over,
                    $"- **{b.Category}** — spent {Money(b.Actual)} against a budget of {Money(b.Budget)} ({b.VariancePct:+0.0;-0.0}%). That's {Money(over)} over."));
            }

            // 2. Anomalies (a category running well above its own normal)
            foreach (var a in s.Anomalies)
            {
                leaks.Add((a.Amount,
                    $"- **{a.Category}** is running hot — {a.Note} About {Money(a.Amount)} above its usual."));
            }

            // 3. Rising-fast categories (>25% MoM, meaningful amount, not already flagged as anomaly)
            foreach (var c in s.Categories
                         .Where(c => c.MoMChangePct.HasValue && c.MoMChangePct > 25 && c.Amount >= 1000)
                         .Where(c => !s.Anomalies.Any(a => a.Category == c.Category)))
            {
                leaks.Add((c.Amount * 0.1m,
                    $"- **{c.Category}** rose {c.MoMChangePct:+0.0;-0.0}% versus last month ({Money(c.Amount)} this month)."));
            }

            // 4. Recurring charges (likely-forgotten subscriptions)
            foreach (var r in s.RecurringCharges)
            {
                leaks.Add((r.Amount,
                    $"- **{r.Merchant}** — recurring {r.Frequency} charge of {Money(r.Amount)} (last on {r.LastSeen:yyyy-MM-dd}). Worth a quick review."));
            }

            if (leaks.Count == 0)
            {
                sb.AppendLine("Good news — no major leaks this month. Nothing went over budget, no category jumped well above its usual, and no surprise recurring charges turned up. 🎉");
                sb.AppendLine();
                return;
            }

            foreach (var (_, line) in leaks.OrderByDescending(x => x.impact))
                sb.AppendLine(line);

            sb.AppendLine();
        }

        // ─── ## How you can save ────────────────────────────────────────────
        private static void WriteSavings(StringBuilder sb, FinancialSnapshot s)
        {
            sb.AppendLine("## How you can save");
            sb.AppendLine();

            if (s.Opportunities.Count == 0)
            {
                sb.AppendLine("Nothing concrete jumped out from this month's numbers. Keep doing what you're doing — your spending pattern looks sustainable.");
                sb.AppendLine();
                return;
            }

            foreach (var o in s.Opportunities)
            {
                var actionVerb = o.Type switch
                {
                    "subscription" => "Cancel or review",
                    "fee"          => "Avoid",
                    "rate"         => "Renegotiate or refinance",
                    "spike"        => "Trim the",
                    "overspend"    => "Pull back the",
                    _              => "Review"
                };

                sb.AppendLine($"- **{actionVerb} {o.Label}** — save **{Money(o.MonthlySaving)}/month** ({o.Difficulty}). {o.Evidence}");
            }

            var totalSaving = s.Opportunities.Sum(o => o.MonthlySaving);
            sb.AppendLine();
            sb.AppendLine($"If you tackle all of these, you could free up around **{Money(totalSaving)} a month** — roughly **{Money(totalSaving * 12)} a year**. That's a real boost to your savings rate. 💪");
            sb.AppendLine();
        }

        // ─── ## A budget to aim for ─────────────────────────────────────────
        private static void WriteBudgetTarget(StringBuilder sb, FinancialSnapshot s)
        {
            sb.AppendLine("## A budget to aim for");
            sb.AppendLine();

            // Take the top 5 categories by current spend
            var top = s.Categories.Take(5).ToList();
            if (top.Count == 0)
            {
                sb.AppendLine("No spending recorded yet — set a few budgets from the Budgets page once your first month's data is in.");
                sb.AppendLine();
                return;
            }

            sb.AppendLine("| Category | Target / month | Why |");
            sb.AppendLine("|---|---|---|");

            foreach (var c in top)
            {
                var (target, why) = SuggestTarget(c, s);
                sb.AppendLine($"| {c.Category} | {Money(target)} | {why} |");
            }

            sb.AppendLine();
        }

        private static (decimal target, string why) SuggestTarget(CategorySpend c, FinancialSnapshot s)
        {
            // 1. If a budget exists and they consistently overspend, suggest a stretch target
            //    midway between current spend and the existing budget — realistic, not punitive.
            var existing = s.Budgets.FirstOrDefault(b => b.Category == c.Category);
            if (existing != null && existing.Actual > existing.Budget)
            {
                var mid = RoundToNearest((existing.Budget + existing.Actual) / 2m, 100);
                return (mid, $"Halfway between your current {Money(existing.Budget)} budget and {Money(existing.Actual)} actual — a realistic stretch.");
            }
            if (existing != null)
            {
                return (existing.Budget, $"You're already on track against your {Money(existing.Budget)} budget — keep it.");
            }

            // 2. Otherwise base on the 3-month trailing average if we have one.
            if (s.TrailingAverages.TryGetValue(c.Category, out var avg) && avg > 0)
            {
                var rounded = RoundToNearest(avg, 100);
                return (rounded, $"Your 3-month average. Steady spend pattern, no change needed.");
            }

            // 3. Fallback — use this month's spend (just established a baseline).
            return (RoundToNearest(c.Amount, 100), $"Based on this month's spend — set a real budget to track it.");
        }

        // ─── ## Do these first ──────────────────────────────────────────────
        private static void WriteDoFirst(StringBuilder sb, FinancialSnapshot s)
        {
            sb.AppendLine("## Do these first");
            sb.AppendLine();

            // Top 3 = highest-saving opportunities, weighted slightly toward easier ones.
            var ranked = s.Opportunities
                .OrderByDescending(o => o.MonthlySaving * DifficultyWeight(o.Difficulty))
                .Take(3)
                .ToList();

            if (ranked.Count == 0)
            {
                sb.AppendLine("1. Keep tracking everything — your habits look healthy this month.");
                sb.AppendLine("2. Consider increasing your savings target if the surplus is consistent.");
                sb.AppendLine("3. Review your budgets monthly so they stay relevant.");
                sb.AppendLine();
                return;
            }

            var i = 1;
            foreach (var o in ranked)
                sb.AppendLine($"{i++}. {ActionLine(o)}");

            sb.AppendLine();
        }

        private static string ActionLine(Opportunity o) => o.Type switch
        {
            "subscription" => $"Review or cancel the **{o.Label}** — saves {Money(o.MonthlySaving)}/month ({o.Difficulty}).",
            "spike"        => $"Trim the **{o.Label}** — saves about {Money(o.MonthlySaving)}/month ({o.Difficulty}).",
            "overspend"    => $"Pull back the **{o.Label}** — saves {Money(o.MonthlySaving)}/month ({o.Difficulty}).",
            "fee"          => $"Avoid the **{o.Label}** — saves {Money(o.MonthlySaving)}/month ({o.Difficulty}).",
            "rate"         => $"Renegotiate **{o.Label}** — redirects {Money(o.MonthlySaving)}/month into savings ({o.Difficulty}).",
            _              => $"**{o.Label}** — saves {Money(o.MonthlySaving)}/month ({o.Difficulty})."
        };

        private static decimal DifficultyWeight(string difficulty) => difficulty switch
        {
            "easy"   => 1.2m,
            "medium" => 1.0m,
            "hard"   => 0.7m,
            _        => 1.0m
        };

        // ─── helpers ────────────────────────────────────────────────────────
        private static string Money(decimal amount)
            => amount.ToString("C0", INR);

        private static decimal RoundToNearest(decimal value, int step)
            => Math.Round(value / step, MidpointRounding.AwayFromZero) * step;

        private static string MonthLabel(string period)
        {
            if (DateTime.TryParseExact(period, "yyyy-MM", CultureInfo.InvariantCulture, DateTimeStyles.None, out var d))
                return d.ToString("MMMM yyyy", CultureInfo.InvariantCulture);
            return period;
        }

        // "May 2026" for full months; "1–15 June 2026" for MTD.
        private static string PeriodLabel(FinancialSnapshot s)
        {
            if (!s.AsOf.HasValue) return MonthLabel(s.Period);
            return $"1–{s.AsOf.Value.Day} {s.AsOf.Value.ToString("MMMM yyyy", CultureInfo.InvariantCulture)}";
        }
    }
}
